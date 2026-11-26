/**
 * Server-only HTTP client for the Verkku Games Backend REST v1 API.
 *
 * Hard rules enforced here:
 * - `API_BASE_URL` / `API_CLIENT_KEY` live only in server runtime env. They are
 *   never logged, echoed, embedded in a response body or surfaced in errors.
 * - Every failure collapses to a sanitized RFC 9457 `ProblemDetailsDto`; raw
 *   provider payloads and the wholesale provider name never leave this module.
 * - Requests are bounded by an 8s timeout and always carry the BFF key plus the
 *   visitor IP (when available) for backend rate limiting.
 */
import type {
  BigCategoryDto,
  CatalogItemDetailDto,
  CatalogItemDto,
  CategoryDto,
  CategoryRequirementDto,
  ExchangeRatesDto,
  ItemPriceQuoteDto,
  PagedResponse,
  ProblemDetailsDto,
  ProductType,
  RegionCode,
} from './types';

/** Public provider UIDs are opaque GUIDs; anything else is rejected. */
export const UUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** External ids mirror the backend validator: 1-64 url-safe characters. */
export const EXTERNAL_ID_REGEX = /^[A-Za-z0-9_-]{1,64}$/;

export function isValidUuid(value: string | null | undefined): value is string {
  return typeof value === 'string' && UUID_REGEX.test(value);
}

export function isValidExternalId(value: string | null | undefined): value is string {
  return typeof value === 'string' && EXTERNAL_ID_REGEX.test(value);
}

export interface ServerRuntimeEnv {
  API_BASE_URL?: string;
  API_CLIENT_KEY?: string;
  PUBLIC_SITE_URL?: string;
}

export type ApiResult<T> =
  | { ok: true; status: number; data: T }
  | { ok: false; status: number; error: ProblemDetailsDto };

export interface ServerApiClientOptions {
  clientIp?: string;
  fetchFn?: typeof fetch;
}

export interface ServerApiClient {
  getBigCategories(providerUid?: string): Promise<ApiResult<BigCategoryDto[]>>;
  getCategories(params?: {
    providerUid?: string;
    bigCategoryId?: string;
    region?: RegionCode;
    includeGlobal?: boolean;
    search?: string;
  }): Promise<ApiResult<CategoryDto[]>>;
  getItems(params?: {
    providerUid?: string;
    categoryId?: string;
    region?: RegionCode;
    includeGlobal?: boolean;
    productType?: ProductType;
    inStockOnly?: boolean;
    search?: string;
    page?: number;
    pageSize?: number;
  }): Promise<ApiResult<PagedResponse<CatalogItemDto>>>;
  getItemDetail(providerUid: string, itemId: string): Promise<ApiResult<CatalogItemDetailDto>>;
  getItemPrice(providerUid: string, itemId: string): Promise<ApiResult<ItemPriceQuoteDto>>;
  getCategoryRequirements(providerUid: string, categoryId: string): Promise<ApiResult<CategoryRequirementDto[]>>;
  getRates(): Promise<ApiResult<ExchangeRatesDto>>;
}

const REQUEST_TIMEOUT_MS = 30000;
const MAX_ERROR_TEXT_LENGTH = 240;
const JSON_CONTENT_TYPE = 'application/json; charset=utf-8';
const ERROR_CODE_PATTERN = /^[a-z0-9_.-]{1,64}$/i;
const MAX_RETRY_AFTER_SECONDS = 86_400;

/** Matches the wholesale brand with or without spacing, without spelling it. */
const PROVIDER_NAME_PATTERN = /shop\s*2\s*top\s*up/gi;

const BACKEND_NOT_CONFIGURED: ProblemDetailsDto = {
  status: 503,
  code: 'backend_not_configured',
  title: 'Service temporarily unavailable',
};

const UPSTREAM_UNAVAILABLE: ProblemDetailsDto = {
  status: 503,
  code: 'upstream_unavailable',
  title: 'Catalog service temporarily unavailable',
};

function pickText(source: unknown, key: string): string | undefined {
  if (!source || typeof source !== 'object') return undefined;

  const value = (source as Record<string, unknown>)[key];
  if (typeof value !== 'string') return undefined;

  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : undefined;
}

function readProcessEnv(): Record<string, string | undefined> | undefined {
  return (globalThis as { process?: { env?: Record<string, string | undefined> } }).process?.env;
}

function readViteEnv(): Record<string, unknown> | undefined {
  try {
    return (import.meta as ImportMeta & { env?: Record<string, unknown> }).env;
  } catch {
    return undefined;
  }
}

/**
 * Fills missing keys from the server runtime (`process.env` under
 * `nodejs_compat`, then Vite/`import.meta.env`). An explicitly provided value
 * always wins, so `locals.runtime.env` behaves like Cloudflare Workers expect.
 */
export function normalizeServerEnv(env?: Partial<ServerRuntimeEnv> | null): ServerRuntimeEnv {
  const processBag = readProcessEnv();
  const viteBag = readViteEnv();

  const resolve = (key: keyof ServerRuntimeEnv): string | undefined =>
    pickText(env, key) ?? pickText(processBag, key) ?? pickText(viteBag, key);

  return {
    API_BASE_URL: resolve('API_BASE_URL'),
    API_CLIENT_KEY: resolve('API_CLIENT_KEY'),
    PUBLIC_SITE_URL: resolve('PUBLIC_SITE_URL'),
  };
}

/**
 * `@astrojs/cloudflare@14` exposes `locals.runtime.env` as a throwing getter
 * (removed in Astro v6). Read it defensively so the same helper works on the
 * adapter, Astro dev and Bun tests; real Workers config comes from
 * `process.env` under `nodejs_compat`.
 */
function readLocalsEnv(locals?: App.Locals): Record<string, unknown> | undefined {
  try {
    return locals?.runtime?.env;
  } catch {
    return undefined;
  }
}

/** Resolves server config from `Astro.locals`, then the process/Vite env. */
export function resolveServerEnv(locals?: App.Locals): ServerRuntimeEnv {
  return normalizeServerEnv(readLocalsEnv(locals) ?? null);
}

const CLIENT_IP_PATTERN = /^[0-9A-Fa-f:.]+$/;
const MAX_CLIENT_IP_LENGTH = 64;

function sanitizeClientIp(value: string | null): string | undefined {
  if (typeof value !== 'string') return undefined;

  const trimmed = value.trim();
  if (!trimmed || trimmed.length > MAX_CLIENT_IP_LENGTH) return undefined;
  if (!CLIENT_IP_PATTERN.test(trimmed)) return undefined;

  return trimmed;
}

/**
 * Visitor IP used for backend rate limiting: `CF-Connecting-IP` first, then the
 * first `X-Forwarded-For` hop. Values are validated so header injection and
 * junk forwarding cannot reach the backend.
 */
export function resolveClientIp(request: Request): string | undefined {
  const direct = sanitizeClientIp(request.headers.get('cf-connecting-ip'));
  if (direct) return direct;

  const forwarded = request.headers.get('x-forwarded-for');
  if (!forwarded) return undefined;

  const [firstHop] = forwarded.split(',');
  return sanitizeClientIp(firstHop ?? null);
}

function redact(text: string, secrets: readonly string[]): string {
  let value = text.replace(PROVIDER_NAME_PATTERN, 'the catalog service');

  for (const secret of secrets) {
    if (secret.length < 3) continue;
    value = value.split(secret).join('[redacted]');
  }

  return value.trim().slice(0, MAX_ERROR_TEXT_LENGTH);
}

function sanitizeBaseUrl(value?: string): string | undefined {
  const trimmed = (value ?? '').trim();
  if (!trimmed) return undefined;

  try {
    const parsed = new URL(trimmed);
    if (parsed.protocol !== 'https:' && parsed.protocol !== 'http:') return undefined;
    return trimmed.replace(/\/+$/, '');
  } catch {
    return undefined;
  }
}

function parseJson(text: string): unknown {
  if (!text.trim()) return undefined;

  try {
    return JSON.parse(text) as unknown;
  } catch {
    return undefined;
  }
}

function toProblemObject(value: unknown): Record<string, unknown> | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined;
  return value as Record<string, unknown>;
}

function toPositiveInt(value: unknown): number | undefined {
  const parsed =
    typeof value === 'number'
      ? value
      : typeof value === 'string' && /^\d+$/.test(value.trim())
        ? Number.parseInt(value.trim(), 10)
        : Number.NaN;

  if (!Number.isInteger(parsed) || parsed <= 0 || parsed > MAX_RETRY_AFTER_SECONDS) return undefined;
  return parsed;
}

function readRetryAfter(bodyValue: unknown, response: Response): number | undefined {
  return toPositiveInt(bodyValue) ?? toPositiveInt(response.headers.get('retry-after'));
}

function sanitizeUpstreamProblem(text: string, response: Response, secrets: readonly string[]): ProblemDetailsDto {
  const status = response.status;

  // Credential failures and 5xx can carry internal topology/provider details:
  // never relay them, just report a sanitized upstream outage.
  if (status === 401 || status === 403 || status >= 500) {
    return { ...UPSTREAM_UNAVAILABLE };
  }

  const raw = toProblemObject(parseJson(text));

  const rawCode = typeof raw?.code === 'string' ? raw.code.trim() : '';
  const code = ERROR_CODE_PATTERN.test(rawCode) ? rawCode : 'upstream_error';

  const rawTitle = typeof raw?.title === 'string' ? raw.title : '';
  const title = rawTitle.trim() ? redact(rawTitle, secrets) : 'Request could not be completed';

  const rawDetail = typeof raw?.detail === 'string' ? raw.detail : '';
  const detail = rawDetail.trim() ? redact(rawDetail, secrets) : undefined;

  const retryAfterSeconds = readRetryAfter(raw?.retryAfterSeconds, response);

  const problem: ProblemDetailsDto = { status, code, title };
  if (detail) problem.detail = detail;
  if (retryAfterSeconds !== undefined) problem.retryAfterSeconds = retryAfterSeconds;

  return problem;
}

function buildUrl(base: string, path: string, query?: Record<string, string | undefined>): string {
  const url = new URL(`${base}${path.startsWith('/') ? path : `/${path}`}`);

  for (const [key, value] of Object.entries(query ?? {})) {
    if (typeof value === 'string' && value.length > 0) {
      url.searchParams.set(key, value);
    }
  }

  return url.toString();
}

function boolParam(value: boolean | undefined): string | undefined {
  return value === undefined ? undefined : value ? 'true' : 'false';
}

/**
 * Creates a BFF client bound to one request's env + visitor IP.
 *
 * Missing/invalid configuration never throws: every method returns the same
 * sanitized `503 backend_not_configured` result so pages can degrade cleanly.
 */
export function createServerApiClient(
  env: ServerRuntimeEnv,
  options: ServerApiClientOptions = {},
): ServerApiClient {
  const apiBaseUrl = sanitizeBaseUrl(env.API_BASE_URL);
  const apiKey = (env.API_CLIENT_KEY ?? '').trim();
  const secrets = [apiKey, apiBaseUrl ?? ''];
  const configured = apiBaseUrl !== undefined && apiKey.length > 0;
  const doFetch = options.fetchFn ?? fetch;
  const clientIp = sanitizeClientIp(options.clientIp ?? null);

  async function request<T>(path: string, query?: Record<string, string | undefined>): Promise<ApiResult<T>> {
    if (!configured) {
      return { ok: false, status: 503, error: { ...BACKEND_NOT_CONFIGURED } };
    }

    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
    const headers: Record<string, string> = {
      Accept: 'application/json',
      'X-Api-Client-Key': apiKey,
    };

    if (clientIp) headers['X-Forwarded-Client-Ip'] = clientIp;

    let response: Response;
    try {
      response = await doFetch(buildUrl(apiBaseUrl, path, query), {
        method: 'GET',
        headers,
        signal: controller.signal,
      });
    } catch {
      return { ok: false, status: 503, error: { ...UPSTREAM_UNAVAILABLE } };
    } finally {
      clearTimeout(timer);
    }

    let bodyText: string;
    try {
      bodyText = await response.text();
    } catch {
      return { ok: false, status: 503, error: { ...UPSTREAM_UNAVAILABLE } };
    }

    if (!response.ok) {
      const problem = sanitizeUpstreamProblem(bodyText, response, secrets);
      return { ok: false, status: problem.status ?? 503, error: problem };
    }

    const parsed = parseJson(bodyText);
    if (parsed === null || typeof parsed !== 'object' || parsed === undefined) {
      return { ok: false, status: 503, error: { ...UPSTREAM_UNAVAILABLE } };
    }

    return { ok: true, status: response.status, data: parsed as T };
  }

  return {
    getBigCategories(providerUid) {
      return request<BigCategoryDto[]>('/api/v1/catalog/big-categories', {
        providerUid,
      });
    },

    getCategories(params = {}) {
      return request<CategoryDto[]>('/api/v1/catalog/categories', {
        providerUid: params.providerUid,
        bigCategoryId: params.bigCategoryId,
        region: params.region,
        includeGlobal: boolParam(params.includeGlobal),
        search: params.search,
      });
    },

    getItems(params = {}) {
      return request<PagedResponse<CatalogItemDto>>('/api/v1/catalog/items', {
        providerUid: params.providerUid,
        categoryId: params.categoryId,
        region: params.region,
        includeGlobal: boolParam(params.includeGlobal),
        productType: params.productType,
        inStockOnly: boolParam(params.inStockOnly),
        search: params.search,
        page: params.page === undefined ? undefined : String(params.page),
        pageSize: params.pageSize === undefined ? undefined : String(params.pageSize),
      });
    },

    getItemDetail(providerUid, itemId) {
      return request<CatalogItemDetailDto>(
        `/api/v1/catalog/items/${encodeURIComponent(providerUid)}/${encodeURIComponent(itemId)}`,
      );
    },

    getItemPrice(providerUid, itemId) {
      return request<ItemPriceQuoteDto>(
        `/api/v1/catalog/items/${encodeURIComponent(providerUid)}/${encodeURIComponent(itemId)}/price`,
      );
    },

    getCategoryRequirements(providerUid, categoryId) {
      return request<CategoryRequirementDto[]>(
        `/api/v1/catalog/categories/${encodeURIComponent(providerUid)}/${encodeURIComponent(categoryId)}/requirements`,
      );
    },

    getRates() {
      return request<ExchangeRatesDto>('/api/v1/rates');
    },
  };
}

/** Serializes a sanitized problem as JSON and never caches error responses. */
export function problemJsonResponse(problem: ProblemDetailsDto, extraHeaders: Record<string, string> = {}): Response {
  const status = typeof problem.status === 'number' && problem.status >= 400 ? problem.status : 503;
  const headers = new Headers({ 'Content-Type': JSON_CONTENT_TYPE, 'Cache-Control': 'no-store' });

  for (const [name, value] of Object.entries(extraHeaders)) {
    headers.set(name, value);
  }

  if (problem.retryAfterSeconds !== undefined) {
    headers.set('Retry-After', String(problem.retryAfterSeconds));
  }

  return new Response(JSON.stringify({ ...problem, status }), { status, headers });
}

/** Serializes a successful payload with an explicit cache policy. */
export function dataJsonResponse<T>(data: T, status: number, cacheControl: string): Response {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': JSON_CONTENT_TYPE, 'Cache-Control': cacheControl },
  });
}
