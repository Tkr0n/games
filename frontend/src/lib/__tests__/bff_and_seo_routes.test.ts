// @ts-ignore -- `bun:test` ships with the Bun runtime; its types are not part of the Astro app tsconfig (see task-4 report).
import { describe, expect, test } from 'bun:test';

import {
  EXTERNAL_ID_REGEX,
  UUID_REGEX,
  createServerApiClient,
  isValidExternalId,
  isValidUuid,
  resolveClientIp,
  resolveServerEnv,
  type ServerRuntimeEnv,
} from '../api-client';
import { handleItemsRequest } from '../../pages/api/items';
import { handlePriceRequest } from '../../pages/api/price';
import { handleRatesRequest } from '../../pages/api/rates';
import { buildRobotsTxt, handleRobotsTxtRequest } from '../../pages/robots.txt';
import { buildSitemapXml, handleSitemapXmlRequest } from '../../pages/sitemap.xml';
import type { CategoryDto, ItemPriceQuoteDto } from '../types';

const SITE = 'https://games.verkku.com';
const API_BASE = 'https://api.internal.verkku.test';
const API_KEY = 'server-only-bff-key-0123456789abcdef';
const PROVIDER_UID = 'd4f8a2c1-7b3e-4f90-9a12-6e8b5c3d1f42';

const ENV: ServerRuntimeEnv = {
  API_BASE_URL: API_BASE,
  API_CLIENT_KEY: API_KEY,
  PUBLIC_SITE_URL: SITE,
};

interface RecordedCall {
  url: string;
  init: RequestInit | undefined;
}

function jsonResponse(payload: unknown, status = 200): Response {
  return new Response(JSON.stringify(payload), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

function recordingFetch(responder: () => Response): { fetchFn: typeof fetch; calls: RecordedCall[] } {
  const calls: RecordedCall[] = [];
  const fetchFn = (async (input: RequestInfo | URL, init?: RequestInit) => {
    calls.push({ url: String(input), init });
    return responder();
  }) as unknown as typeof fetch;

  return { fetchFn, calls };
}

function failingFetch(): { fetchFn: typeof fetch; calls: RecordedCall[] } {
  const calls: RecordedCall[] = [];
  const fetchFn = (async (input: RequestInfo | URL) => {
    calls.push({ url: String(input), init: undefined });
    throw new Error(`connect ECONNREFUSED ${API_BASE} X-Api-Client-Key: ${API_KEY}`);
  }) as unknown as typeof fetch;

  return { fetchFn, calls };
}

/** Bun/Bun-compatible global bag; avoids depending on `@types/node` in `astro check`. */
const globalProcess = (globalThis as { process?: { env?: Record<string, string | undefined> } }).process;

async function withoutBackendEnv<T>(fn: () => Promise<T>): Promise<T> {
  const env = globalProcess?.env;
  if (!env) return fn();

  const saved = { base: env.API_BASE_URL, key: env.API_CLIENT_KEY, site: env.PUBLIC_SITE_URL };
  delete env.API_BASE_URL;
  delete env.API_CLIENT_KEY;
  delete env.PUBLIC_SITE_URL;

  try {
    return await fn();
  } finally {
    if (saved.base === undefined) delete env.API_BASE_URL;
    else env.API_BASE_URL = saved.base;
    if (saved.key === undefined) delete env.API_CLIENT_KEY;
    else env.API_CLIENT_KEY = saved.key;
    if (saved.site === undefined) delete env.PUBLIC_SITE_URL;
    else env.PUBLIC_SITE_URL = saved.site;
  }
}

describe('server api client', () => {
  test('sends the BFF key and the forwarded visitor IP, never from the browser', async () => {
    const { fetchFn, calls } = recordingFetch(() => jsonResponse([]));
    const client = createServerApiClient(ENV, { clientIp: '203.0.113.9', fetchFn });

    const result = await client.getCategories({ region: 'USA', includeGlobal: true });

    expect(result.ok).toBe(true);
    expect(calls.length).toBe(1);

    const headers = new Headers(calls[0]!.init?.headers);
    expect(headers.get('accept')).toBe('application/json');
    expect(headers.get('x-api-client-key')).toBe(API_KEY);
    expect(headers.get('x-forwarded-client-ip')).toBe('203.0.113.9');

    const url = new URL(calls[0]!.url);
    expect(`${url.origin}${url.pathname}`).toBe(`${API_BASE}/api/v1/catalog/categories`);
    expect(url.searchParams.get('region')).toBe('USA');
    expect(url.searchParams.get('includeGlobal')).toBe('true');
  });

  test('omits the forwarded IP header when no visitor IP is available', async () => {
    const { fetchFn, calls } = recordingFetch(() => jsonResponse([]));
    const client = createServerApiClient(ENV, { fetchFn });

    await client.getRates();

    const headers = new Headers(calls[0]!.init?.headers);
    expect(headers.get('x-api-client-key')).toBe(API_KEY);
    expect(headers.get('x-forwarded-client-ip')).toBeNull();
  });

  test('returns a sanitized 503 when the backend is not configured', async () => {
    await withoutBackendEnv(async () => {
      const client = createServerApiClient({ API_BASE_URL: '', API_CLIENT_KEY: '' });
      const result = await client.getItems();

      expect(result.ok).toBe(false);
      if (result.ok) throw new Error('expected a failure result');

      expect(result.status).toBe(503);
      expect(result.error.status).toBe(503);
      expect(result.error.code).toBe('backend_not_configured');

      const serialized = JSON.stringify(result);
      expect(serialized).not.toContain(API_KEY);
      expect(serialized).not.toContain(API_BASE);
      expect(serialized.toLowerCase()).not.toContain('shop2topup');
    });
  });

  test('collapses network failures into a sanitized upstream_unavailable', async () => {
    const { fetchFn } = failingFetch();
    const client = createServerApiClient(ENV, { clientIp: '198.51.100.7', fetchFn });

    const result = await client.getItemPrice(PROVIDER_UID, 'roblox-1000');

    expect(result.ok).toBe(false);
    if (result.ok) throw new Error('expected a failure result');

    expect(result.status).toBe(503);
    expect(result.error.code).toBe('upstream_unavailable');

    const serialized = JSON.stringify(result);
    expect(serialized).not.toContain(API_KEY);
    expect(serialized).not.toContain(API_BASE);
    expect(serialized.toLowerCase()).not.toContain('shop2topup');
  });

  test('replaces upstream 5xx bodies with a sanitized 503 (no key, URL or provider)', async () => {
    const { fetchFn } = recordingFetch(
      () =>
        new Response(
          JSON.stringify({
            status: 500,
            code: 'provider_failure',
            title: `Upstream ${API_BASE} failed`,
            detail: `X-Api-Client-Key: ${API_KEY} at SHOP2TOPUP`,
          }),
          { status: 500, headers: { 'content-type': 'application/json' } },
        ),
    );
    const client = createServerApiClient(ENV, { fetchFn });

    const result = await client.getBigCategories();

    expect(result.ok).toBe(false);
    if (result.ok) throw new Error('expected a failure result');

    expect(result.status).toBe(503);
    expect(result.error.code).toBe('upstream_unavailable');

    const serialized = JSON.stringify(result).toLowerCase();
    expect(serialized).not.toContain(API_KEY.toLowerCase());
    expect(serialized).not.toContain(API_BASE.toLowerCase());
    expect(serialized).not.toContain('shop2topup');
  });

  test('returns a sanitized 503 on non-JSON upstream payloads', async () => {
    const { fetchFn } = recordingFetch(() => new Response('<html>gateway</html>', { status: 200 }));
    const client = createServerApiClient(ENV, { fetchFn });

    const result = await client.getRates();

    expect(result.ok).toBe(false);
    if (result.ok) throw new Error('expected a failure result');
    expect(result.status).toBe(503);
    expect(result.error.code).toBe('upstream_unavailable');
  });

  test('builds every documented backend path', async () => {
    const { fetchFn, calls } = recordingFetch(() => jsonResponse({}));
    const client = createServerApiClient(ENV, { fetchFn });

    await client.getBigCategories();
    await client.getItems({ categoryId: 'roblox', productType: 'voucher', inStockOnly: true, page: 2, pageSize: 10 });
    await client.getItemDetail(PROVIDER_UID, 'roblox-1000');
    await client.getItemPrice(PROVIDER_UID, 'roblox-1000');
    await client.getCategoryRequirements(PROVIDER_UID, 'roblox');
    await client.getRates();

    expect(calls.map((call) => new URL(call.url).pathname)).toEqual([
      '/api/v1/catalog/big-categories',
      '/api/v1/catalog/items',
      `/api/v1/catalog/items/${PROVIDER_UID}/roblox-1000`,
      `/api/v1/catalog/items/${PROVIDER_UID}/roblox-1000/price`,
      `/api/v1/catalog/categories/${PROVIDER_UID}/roblox/requirements`,
      '/api/v1/rates',
    ]);

    const itemsUrl = new URL(calls[1]!.url);
    expect(itemsUrl.searchParams.get('productType')).toBe('voucher');
    expect(itemsUrl.searchParams.get('inStockOnly')).toBe('true');
    expect(itemsUrl.searchParams.get('page')).toBe('2');
    expect(itemsUrl.searchParams.get('pageSize')).toBe('10');
  });
});

describe('resolveClientIp', () => {
  test('prefers CF-Connecting-IP', () => {
    const request = new Request('https://games.verkku.com/', {
      headers: {
        'cf-connecting-ip': '198.51.100.4',
        'x-forwarded-for': '203.0.113.9, 10.0.0.1',
      },
    });

    expect(resolveClientIp(request)).toBe('198.51.100.4');
  });

  test('falls back to the first X-Forwarded-For hop, trimmed', () => {
    const request = new Request('https://games.verkku.com/', {
      headers: { 'x-forwarded-for': '  203.0.113.9 , 10.0.0.1' },
    });

    expect(resolveClientIp(request)).toBe('203.0.113.9');
  });

  test('rejects missing, oversized or non-IP values', () => {
    expect(resolveClientIp(new Request('https://games.verkku.com/'))).toBeUndefined();
    expect(
      resolveClientIp(
        new Request('https://games.verkku.com/', { headers: { 'cf-connecting-ip': 'a'.repeat(65) } }),
      ),
    ).toBeUndefined();
    expect(
      resolveClientIp(
        new Request('https://games.verkku.com/', { headers: { 'cf-connecting-ip': 'not-an-ip;drop' } }),
      ),
    ).toBeUndefined();
    expect(
      resolveClientIp(new Request('https://games.verkku.com/', { headers: { 'x-forwarded-for': 'unknown, 10.0.0.1' } })),
    ).toBeUndefined();
  });
});

describe('resolveServerEnv', () => {
  test('reads config from locals.runtime.env when the adapter provides it', async () => {
    await withoutBackendEnv(async () => {
      const locals = {
        runtime: { env: { API_BASE_URL: 'https://local.internal.test', API_CLIENT_KEY: 'local-key-123' } },
      } as unknown as Parameters<typeof resolveServerEnv>[0];

      const env = resolveServerEnv(locals);

      expect(env.API_BASE_URL).toBe('https://local.internal.test');
      expect(env.API_CLIENT_KEY).toBe('local-key-123');
    });
  });

  test('survives the @astrojs/cloudflare@14 throwing runtime.env getter', async () => {
    await withoutBackendEnv(async () => {
      const locals = {
        runtime: {
          get env(): Record<string, string | undefined> {
            throw new Error('Astro.locals.runtime.env has been removed in Astro v6.');
          },
        },
      } as unknown as Parameters<typeof resolveServerEnv>[0];

      expect(() => resolveServerEnv(locals)).not.toThrow();
      expect(resolveServerEnv(locals).API_BASE_URL).toBeUndefined();
    });
  });
});

describe('strict identifier validators', () => {
  test('accept only canonical UUIDs and external ids', () => {
    expect(UUID_REGEX.test(PROVIDER_UID)).toBe(true);
    expect(isValidUuid(PROVIDER_UID)).toBe(true);
    expect(isValidUuid(PROVIDER_UID.toUpperCase())).toBe(true);
    expect(isValidUuid('shop2topup')).toBe(false);
    expect(isValidUuid('../admin')).toBe(false);
    expect(isValidUuid(null)).toBe(false);
    expect(isValidUuid(undefined)).toBe(false);

    expect(EXTERNAL_ID_REGEX.test('roblox-1000_X')).toBe(true);
    expect(isValidExternalId('roblox-1000')).toBe(true);
    expect(isValidExternalId('../admin')).toBe(false);
    expect(isValidExternalId('a'.repeat(65))).toBe(false);
    expect(isValidExternalId('')).toBe(false);
  });
});

describe('GET /api/price', () => {
  const quote: ItemPriceQuoteDto = {
    providerUid: PROVIDER_UID,
    externalItemId: 'roblox-1000',
    itemName: 'Roblox 1,000 Robux',
    retailPriceUsd: { amount: 9.99, currency: 'USD' },
    inStock: true,
    quotedAtUtc: '2026-11-25T00:00:00Z',
  };

  test('rejects invalid identifiers with 400 before touching the backend', async () => {
    const { fetchFn, calls } = recordingFetch(() => jsonResponse(quote));

    const queries = [
      'providerUid=shop2topup&itemId=roblox-1000',
      'providerUid=../admin&itemId=roblox-1000',
      `providerUid=${PROVIDER_UID}&itemId=../admin`,
      `providerUid=${PROVIDER_UID}&itemId=javascript:alert(1)`,
      `providerUid=${PROVIDER_UID}`,
      'providerUid=&itemId=roblox-1000',
    ];

    for (const query of queries) {
      const response = await handlePriceRequest(new Request(`https://games.verkku.com/api/price?${query}`), ENV, fetchFn);

      expect(response.status).toBe(400);
      expect(response.headers.get('cache-control')).toBe('no-store');
      const body = (await response.json()) as { status?: number; code?: string };
      expect(body.status).toBe(400);
      expect(body.code).toBe('invalid_request');
    }

    expect(calls.length).toBe(0);
  });

  test('proxies a valid quote as no-store JSON', async () => {
    const { fetchFn, calls } = recordingFetch(() => jsonResponse(quote));

    const response = await handlePriceRequest(
      new Request(`https://games.verkku.com/api/price?providerUid=${PROVIDER_UID}&itemId=roblox-1000`),
      ENV,
      fetchFn,
    );

    expect(response.status).toBe(200);
    expect(response.headers.get('cache-control')).toBe('no-store');
    expect(response.headers.get('content-type')).toBe('application/json; charset=utf-8');

    const body = (await response.json()) as ItemPriceQuoteDto;
    expect(body.retailPriceUsd.amount).toBe(9.99);

    expect(calls.length).toBe(1);
    expect(calls[0]!.url).toBe(`${API_BASE}/api/v1/catalog/items/${PROVIDER_UID}/roblox-1000/price`);
  });
});

describe('GET /api/items', () => {
  test('rejects invalid paging, region, search and identifier input before the backend', async () => {
    const { fetchFn, calls } = recordingFetch(() => jsonResponse({ items: [] }));

    const queries = [
      'pageSize=101',
      'pageSize=0',
      'page=0',
      'page=1.5',
      'page=abc',
      'region=MARS',
      'search=' + 'a'.repeat(101),
      'providerUid=shop2topup',
      'providerUid=../admin',
      'categoryId=../admin',
      'includeGlobal=yes',
      'inStockOnly=1',
      'productType=dlc',
    ];

    for (const query of queries) {
      const response = await handleItemsRequest(new Request(`https://games.verkku.com/api/items?${query}`), ENV, fetchFn);

      expect(response.status).toBe(400);
      expect(response.headers.get('cache-control')).toBe('no-store');
    }

    expect(calls.length).toBe(0);
  });

  test('applies safe defaults and caches the catalog list for 30 seconds', async () => {
    const { fetchFn, calls } = recordingFetch(() =>
      jsonResponse({ items: [], page: 1, pageSize: 24, totalCount: 0, totalPages: 0 }),
    );

    const response = await handleItemsRequest(new Request('https://games.verkku.com/api/items'), ENV, fetchFn);

    expect(response.status).toBe(200);
    expect(response.headers.get('cache-control')).toBe('public, max-age=30');

    expect(calls.length).toBe(1);
    const url = new URL(calls[0]!.url);
    expect(`${url.origin}${url.pathname}`).toBe(`${API_BASE}/api/v1/catalog/items`);
    expect(url.searchParams.get('page')).toBe('1');
    expect(url.searchParams.get('pageSize')).toBe('24');
    expect(url.searchParams.get('includeGlobal')).toBe('false');
    expect(url.searchParams.get('inStockOnly')).toBe('false');
    expect(url.searchParams.get('region')).toBeNull();
  });

  test('forwards valid filters untouched', async () => {
    const { fetchFn, calls } = recordingFetch(() =>
      jsonResponse({ items: [], page: 2, pageSize: 10, totalCount: 0, totalPages: 0 }),
    );

    const query = new URLSearchParams({
      providerUid: PROVIDER_UID,
      categoryId: 'roblox',
      region: 'MX',
      includeGlobal: 'true',
      productType: 'topup',
      inStockOnly: 'true',
      search: 'robux',
      page: '2',
      pageSize: '10',
    });

    const response = await handleItemsRequest(
      new Request(`https://games.verkku.com/api/items?${query.toString()}`),
      ENV,
      fetchFn,
    );

    expect(response.status).toBe(200);

    const url = new URL(calls[0]!.url);
    expect(url.searchParams.get('providerUid')).toBe(PROVIDER_UID);
    expect(url.searchParams.get('categoryId')).toBe('roblox');
    expect(url.searchParams.get('region')).toBe('MX');
    expect(url.searchParams.get('includeGlobal')).toBe('true');
    expect(url.searchParams.get('productType')).toBe('topup');
    expect(url.searchParams.get('inStockOnly')).toBe('true');
    expect(url.searchParams.get('search')).toBe('robux');
    expect(url.searchParams.get('page')).toBe('2');
    expect(url.searchParams.get('pageSize')).toBe('10');
  });
});

describe('GET /api/rates', () => {
  test('caches exchange rates for 30 seconds', async () => {
    const { fetchFn, calls } = recordingFetch(() =>
      jsonResponse({ baseCurrency: 'USD', ratesPerUsd: { USD: 1, BTC: 0.00001 }, retrievedAtUtc: '2026-11-25T00:00:00Z' }),
    );

    const response = await handleRatesRequest(new Request('https://games.verkku.com/api/rates'), ENV, fetchFn);

    expect(response.status).toBe(200);
    expect(response.headers.get('cache-control')).toBe('public, max-age=30');
    expect(calls[0]!.url).toBe(`${API_BASE}/api/v1/rates`);
  });

  test('propagates Retry-After on a backend 429', async () => {
    const { fetchFn } = recordingFetch(() =>
      new Response(
        JSON.stringify({ status: 429, code: 'rate_limited', title: 'Too many requests', retryAfterSeconds: 17 }),
        { status: 429, headers: { 'content-type': 'application/json' } },
      ),
    );

    const response = await handleRatesRequest(new Request('https://games.verkku.com/api/rates'), ENV, fetchFn);

    expect(response.status).toBe(429);
    expect(response.headers.get('retry-after')).toBe('17');
    expect(response.headers.get('cache-control')).toBe('no-store');
  });

  test('propagates the Retry-After header when the backend only sends the header', async () => {
    const { fetchFn } = recordingFetch(
      () =>
        new Response(JSON.stringify({ status: 429, code: 'rate_limited', title: 'Too many requests' }), {
          status: 429,
          headers: { 'content-type': 'application/json', 'retry-after': '42' },
        }),
    );

    const response = await handleRatesRequest(new Request('https://games.verkku.com/api/rates'), ENV, fetchFn);

    expect(response.status).toBe(429);
    expect(response.headers.get('retry-after')).toBe('42');
  });
});

describe('robots.txt', () => {
  test('allows both locales, blocks the BFF and points at the sitemap', () => {
    const robots = buildRobotsTxt(`${SITE}/`);

    expect(robots).toContain('User-agent: *');
    expect(robots).toContain('Allow: /es/');
    expect(robots).toContain('Allow: /en/');
    expect(robots).toContain('Disallow: /api/');
    expect(robots).toContain(`Sitemap: ${SITE}/sitemap.xml`);
    expect(robots.toLowerCase()).not.toContain('shop2topup');
  });

  test('serves plain text with an hour of caching', async () => {
    const response = handleRobotsTxtRequest(new Request(`${SITE}/robots.txt`), ENV);

    expect(response.status).toBe(200);
    expect(response.headers.get('content-type')).toBe('text/plain; charset=utf-8');
    expect(response.headers.get('cache-control')).toBe('public, max-age=3600');

    const body = await response.text();
    expect(body).toContain('Disallow: /api/');
    expect(body).toContain(`Sitemap: ${SITE}/sitemap.xml`);
  });
});

describe('sitemap.xml', () => {
  const categories: CategoryDto[] = [
    {
      providerUid: PROVIDER_UID,
      externalId: 'roblox',
      bigCategoryId: 'gaming',
      name: 'Roblox',
      description: 'Robux top-ups',
      imageUrl: null,
      region: 'USA',
    },
    {
      providerUid: PROVIDER_UID,
      externalId: 'roblox',
      bigCategoryId: 'gaming',
      name: 'Roblox',
      description: 'Robux top-ups',
      imageUrl: null,
      region: 'MX',
    },
    {
      providerUid: PROVIDER_UID,
      externalId: 'steam-wallet',
      bigCategoryId: 'gaming',
      name: 'Steam Wallet',
      description: 'Steam wallet codes',
      imageUrl: null,
      region: 'GLOBAL',
    },
  ];

  test('emits bilingual static and dynamic routes with xhtml alternates', () => {
    const xml = buildSitemapXml(`${SITE}/`, categories);

    expect(xml.startsWith('<?xml version="1.0" encoding="UTF-8"?>')).toBe(true);
    expect(xml).toContain('xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"');
    expect(xml).toContain('xmlns:xhtml="http://www.w3.org/1999/xhtml"');

    for (const route of ['/es', '/en', '/es/catalog', '/en/catalog', '/es/studio', '/en/studio']) {
      expect(xml).toContain(`<loc>${SITE}${route}</loc>`);
    }

    expect(xml).toContain(`<loc>${SITE}/es/category/${PROVIDER_UID}/roblox</loc>`);
    expect(xml).toContain(`<loc>${SITE}/en/category/${PROVIDER_UID}/roblox</loc>`);
    expect(xml).toContain(`<loc>${SITE}/es/category/${PROVIDER_UID}/steam-wallet</loc>`);

    expect(xml).toContain('hreflang="es"');
    expect(xml).toContain('hreflang="en"');
    expect(xml).toContain('hreflang="x-default"');

    // Region duplicates collapse to one bilingual pair (es + en).
    const robloxDynamic = xml.split(`/category/${PROVIDER_UID}/roblox</loc>`).length - 1;
    expect(robloxDynamic).toBe(2);

    expect(xml.toLowerCase()).not.toContain('shop2topup');
  });

  test('drops categories with unsafe or provider-tainted identifiers', () => {
    const tainted: CategoryDto[] = [
      { ...categories[0]!, externalId: 'javascript:alert(1)' },
      { ...categories[0]!, externalId: 'shop2topup' },
      { ...categories[0]!, providerUid: 'not-a-uuid' },
    ];

    const xml = buildSitemapXml(SITE, tainted);

    expect(xml).not.toContain('/category/');
    expect(xml.toLowerCase()).not.toContain('shop2topup');
    expect(xml).not.toContain('javascript');
  });

  test('never repeats a URL', () => {
    const xml = buildSitemapXml(SITE, [...categories, ...categories]);
    const locs = [...xml.matchAll(/<loc>([^<]+)<\/loc>/g)].map((match) => match[1]!);

    expect(locs.length).toBe(new Set(locs).size);
    expect(locs.length).toBe(6 + 2 * 2);
  });

  test('falls back to static routes when the backend is unreachable', async () => {
    const { fetchFn } = failingFetch();

    const response = await handleSitemapXmlRequest(new Request(`${SITE}/sitemap.xml`), ENV, fetchFn);

    expect(response.status).toBe(200);
    expect(response.headers.get('content-type')).toBe('application/xml; charset=utf-8');
    expect(response.headers.get('cache-control')).toBe('public, max-age=900, stale-while-revalidate=300');

    const xml = await response.text();
    expect(xml).toContain(`<loc>${SITE}/es/catalog</loc>`);
    expect(xml).toContain(`<loc>${SITE}/en/studio</loc>`);
    expect(xml).not.toContain('/category/');
  });

  test('includes dynamic categories when the backend responds', async () => {
    const { fetchFn, calls } = recordingFetch(() => jsonResponse(categories));

    const response = await handleSitemapXmlRequest(new Request(`${SITE}/sitemap.xml`), ENV, fetchFn);
    const xml = await response.text();

    expect(response.status).toBe(200);
    expect(xml).toContain(`<loc>${SITE}/es/category/${PROVIDER_UID}/roblox</loc>`);
    expect(xml).toContain('hreflang="x-default"');
    expect(calls[0]!.url).toBe(`${API_BASE}/api/v1/catalog/categories`);
  });

  test('never crashes or leaks when the backend is misconfigured', async () => {
    await withoutBackendEnv(async () => {
      const response = await handleSitemapXmlRequest(new Request(`${SITE}/sitemap.xml`), {});

      expect(response.status).toBe(200);
      const xml = await response.text();
      expect(xml).toContain(`<loc>${SITE}/es/catalog</loc>`);
      expect(xml.toLowerCase()).not.toContain('shop2topup');
    });
  });
});
