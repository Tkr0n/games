import type { APIRoute } from 'astro';

import {
  createServerApiClient,
  dataJsonResponse,
  isValidExternalId,
  isValidUuid,
  normalizeServerEnv,
  problemJsonResponse,
  resolveClientIp,
  resolveServerEnv,
  type ServerRuntimeEnv,
} from '../../lib/api-client';
import { isValidRegionCode } from '../../lib/regions';
import type { ProductType, RegionCode } from '../../lib/types';

const DEFAULT_PAGE = 1;
const DEFAULT_PAGE_SIZE = 24;
const MAX_PAGE_SIZE = 100;
const MAX_SEARCH_LENGTH = 100;
const CACHE_CONTROL = 'public, max-age=30';

const INTEGER_PATTERN = /^\d+$/;

function readParam(params: URLSearchParams, name: string): string | undefined {
  const value = params.get(name);
  if (value === null) return undefined;

  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : undefined;
}

function invalidRequest(detail: string): Response {
  return problemJsonResponse({
    status: 400,
    code: 'invalid_request',
    title: 'Invalid request',
    detail,
  });
}

function readBoolean(params: URLSearchParams, name: string): boolean | 'invalid' | undefined {
  const value = readParam(params, name);
  if (value === undefined) return undefined;
  if (value === 'true') return true;
  if (value === 'false') return false;
  return 'invalid';
}

function readInteger(
  params: URLSearchParams,
  name: string,
  min: number,
  max: number,
): number | 'invalid' | undefined {
  const value = readParam(params, name);
  if (value === undefined) return undefined;
  if (!INTEGER_PATTERN.test(value)) return 'invalid';

  const parsed = Number.parseInt(value, 10);
  if (!Number.isSafeInteger(parsed) || parsed < min || parsed > max) return 'invalid';

  return parsed;
}

/**
 * Pure, testable handler for `GET /api/items`.
 *
 * Every query parameter is whitelisted and validated before the backend is
 * called, so nothing arbitrary is ever proxied to the catalog service.
 */
export async function handleItemsRequest(
  request: Request,
  env: ServerRuntimeEnv = {},
  fetchFn?: typeof fetch,
): Promise<Response> {
  const params = new URL(request.url).searchParams;

  const providerUid = readParam(params, 'providerUid');
  if (providerUid !== undefined && !isValidUuid(providerUid)) {
    return invalidRequest('providerUid must be a valid UUID.');
  }

  const categoryId = readParam(params, 'categoryId');
  if (categoryId !== undefined && !isValidExternalId(categoryId)) {
    return invalidRequest('categoryId must be 1-64 letters, digits, underscores or hyphens.');
  }

  const rawRegion = readParam(params, 'region');
  let region: RegionCode | undefined;
  if (rawRegion !== undefined) {
    if (!isValidRegionCode(rawRegion)) {
      return invalidRequest('region must be a supported region code.');
    }
    region = rawRegion;
  }

  const includeGlobal = readBoolean(params, 'includeGlobal');
  if (includeGlobal === 'invalid') {
    return invalidRequest('includeGlobal must be "true" or "false".');
  }

  const inStockOnly = readBoolean(params, 'inStockOnly');
  if (inStockOnly === 'invalid') {
    return invalidRequest('inStockOnly must be "true" or "false".');
  }

  const rawProductType = readParam(params, 'productType');
  let productType: ProductType | undefined;
  if (rawProductType !== undefined) {
    if (rawProductType !== 'topup' && rawProductType !== 'voucher') {
      return invalidRequest('productType must be "topup" or "voucher".');
    }
    productType = rawProductType;
  }

  const search = readParam(params, 'search');
  if (search !== undefined && search.length > MAX_SEARCH_LENGTH) {
    return invalidRequest(`search must be at most ${MAX_SEARCH_LENGTH} characters.`);
  }

  const page = readInteger(params, 'page', 1, Number.MAX_SAFE_INTEGER);
  if (page === 'invalid') {
    return invalidRequest('page must be an integer greater than or equal to 1.');
  }

  const pageSize = readInteger(params, 'pageSize', 1, MAX_PAGE_SIZE);
  if (pageSize === 'invalid') {
    return invalidRequest(`pageSize must be an integer between 1 and ${MAX_PAGE_SIZE}.`);
  }

  const ATTRIBUTE_PATTERN = /^[A-Za-z0-9 _-]{1,64}$/;
  const platform = readParam(params, 'platform');
  if (platform !== undefined && !ATTRIBUTE_PATTERN.test(platform)) {
    return invalidRequest('platform must be 1-64 letters, digits, spaces, underscores or hyphens.');
  }
  const genre = readParam(params, 'genre');
  if (genre !== undefined && !ATTRIBUTE_PATTERN.test(genre)) {
    return invalidRequest('genre must be 1-64 letters, digits, spaces, underscores or hyphens.');
  }
  const yearFrom = readInteger(params, 'yearFrom', 1970, 2100);
  if (yearFrom === 'invalid') return invalidRequest('yearFrom must be between 1970 and 2100.');
  const yearTo = readInteger(params, 'yearTo', 1970, 2100);
  if (yearTo === 'invalid') return invalidRequest('yearTo must be between 1970 and 2100.');
  const multiplayerOnly = readBoolean(params, 'multiplayerOnly');
  if (multiplayerOnly === 'invalid') {
    return invalidRequest('multiplayerOnly must be "true" or "false".');
  }

  const client = createServerApiClient(normalizeServerEnv(env), {
    clientIp: resolveClientIp(request),
    fetchFn,
  });

  const result = await client.getItems({
    providerUid,
    categoryId,
    region,
    includeGlobal: includeGlobal ?? false,
    productType,
    inStockOnly: inStockOnly ?? false,
    search,
    page: page ?? DEFAULT_PAGE,
    pageSize: pageSize ?? DEFAULT_PAGE_SIZE,
    platform,
    genre,
    yearFrom: yearFrom === undefined ? undefined : yearFrom,
    yearTo: yearTo === undefined ? undefined : yearTo,
    multiplayerOnly: multiplayerOnly ?? false,
  });

  if (!result.ok) {
    return problemJsonResponse(result.error);
  }

  return dataJsonResponse(result.data, result.status, CACHE_CONTROL);
}

export const GET: APIRoute = ({ request, locals }) => handleItemsRequest(request, resolveServerEnv(locals));
