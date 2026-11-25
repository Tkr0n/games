import type { APIRoute } from 'astro';

import { LOCALES, type Locale } from '../i18n/config';
import {
  createServerApiClient,
  isValidExternalId,
  isValidUuid,
  normalizeServerEnv,
  resolveClientIp,
  resolveServerEnv,
  type ServerRuntimeEnv,
} from '../lib/api-client';
import { buildHreflangLinks, normalizeSiteUrl } from '../lib/seo';
import type { CategoryDto } from '../lib/types';

const CONTENT_TYPE = 'application/xml; charset=utf-8';
const CACHE_CONTROL = 'public, max-age=900, stale-while-revalidate=300';

const SITEMAP_NS = 'http://www.sitemaps.org/schemas/sitemap/0.9';
const XHTML_NS = 'http://www.w3.org/1999/xhtml';

/** Locale-agnostic surfaces emitted for every locale. */
const STATIC_PATHS = ['', '/catalog', '/studio'] as const;

/** Matches the wholesale brand with or without spacing, without spelling it. */
const PROVIDER_NAME_PATTERN = /shop\s*2\s*top\s*up/i;

function xmlEscape(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&apos;');
}

function renderUrl(loc: string, alternates: Array<{ hreflang: string; href: string }>): string {
  const links = alternates
    .map(
      (alternate) =>
        `    <xhtml:link rel="alternate" hreflang="${alternate.hreflang}" href="${xmlEscape(alternate.href)}"/>`,
    )
    .join('\n');

  return `  <url>\n    <loc>${xmlEscape(loc)}</loc>\n${links}\n  </url>`;
}

function localizedPath(base: string, locale: Locale, path: string): string {
  return `${base}/${locale}${path}`;
}

/**
 * Pure builder for `sitemap.xml`.
 *
 * Only opaque `providerUid` + external ids ever reach the XML: names,
 * descriptions and the wholesale brand are never serialized. Duplicated
 * categories (same id across regions) collapse into one bilingual pair.
 */
export function buildSitemapXml(siteUrl: string, categories: CategoryDto[]): string {
  const base = normalizeSiteUrl(siteUrl);
  const entries: string[] = [];
  const seen = new Set<string>();

  const append = (path: string, loc: string): void => {
    if (seen.has(loc)) return;
    seen.add(loc);
    entries.push(renderUrl(loc, buildHreflangLinks(base, path)));
  };

  for (const path of STATIC_PATHS) {
    for (const locale of LOCALES) {
      append(path, localizedPath(base, locale, path));
    }
  }

  const list = Array.isArray(categories) ? categories : [];

  for (const category of list) {
    if (!category || typeof category !== 'object') continue;

    const providerUid = typeof category.providerUid === 'string' ? category.providerUid : '';
    const externalId = typeof category.externalId === 'string' ? category.externalId : '';

    if (!isValidUuid(providerUid) || !isValidExternalId(externalId)) continue;
    if (PROVIDER_NAME_PATTERN.test(providerUid) || PROVIDER_NAME_PATTERN.test(externalId)) continue;

    const path = `/category/${encodeURIComponent(providerUid)}/${encodeURIComponent(externalId)}`;

    for (const locale of LOCALES) {
      append(path, localizedPath(base, locale, path));
    }
  }

  return [
    '<?xml version="1.0" encoding="UTF-8"?>',
    `<urlset xmlns="${SITEMAP_NS}" xmlns:xhtml="${XHTML_NS}">`,
    ...entries,
    '</urlset>',
    '',
  ].join('\n');
}

function resolveSiteUrl(request: Request, env: ServerRuntimeEnv): string {
  const configured = normalizeSiteUrl(env.PUBLIC_SITE_URL ?? '');
  if (configured) return configured;

  try {
    return normalizeSiteUrl(new URL(request.url).origin);
  } catch {
    return '';
  }
}

/**
 * Sitemap handler: a backend outage degrades to the static routes instead of a
 * 500, because a partial sitemap is always better than a broken one.
 */
export async function handleSitemapXmlRequest(
  request: Request,
  env: ServerRuntimeEnv = {},
  fetchFn?: typeof fetch,
): Promise<Response> {
  const resolved = normalizeServerEnv(env);
  let categories: CategoryDto[] = [];

  try {
    const client = createServerApiClient(resolved, {
      clientIp: resolveClientIp(request),
      fetchFn,
    });

    const result = await client.getCategories();
    if (result.ok && Array.isArray(result.data)) {
      categories = result.data;
    }
  } catch {
    categories = [];
  }

  const xml = buildSitemapXml(resolveSiteUrl(request, resolved), categories);

  return new Response(xml, {
    status: 200,
    headers: { 'Content-Type': CONTENT_TYPE, 'Cache-Control': CACHE_CONTROL },
  });
}

export const GET: APIRoute = ({ request, locals }) => handleSitemapXmlRequest(request, resolveServerEnv(locals));
