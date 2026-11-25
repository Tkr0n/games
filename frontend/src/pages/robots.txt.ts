import type { APIRoute } from 'astro';

import { normalizeServerEnv, resolveServerEnv, type ServerRuntimeEnv } from '../lib/api-client';
import { normalizeSiteUrl } from '../lib/seo';

const CONTENT_TYPE = 'text/plain; charset=utf-8';
const CACHE_CONTROL = 'public, max-age=3600';

/**
 * Pure builder so the crawl policy can be asserted in Bun tests.
 * `/api/` is a server-only BFF surface: it must never be indexed.
 */
export function buildRobotsTxt(siteUrl: string): string {
  const base = normalizeSiteUrl(siteUrl);

  return ['User-agent: *', 'Allow: /es/', 'Allow: /en/', 'Disallow: /api/', '', `Sitemap: ${base}/sitemap.xml`, ''].join(
    '\n',
  );
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

export function handleRobotsTxtRequest(request: Request, env: ServerRuntimeEnv = {}): Response {
  const siteUrl = resolveSiteUrl(request, normalizeServerEnv(env));

  return new Response(buildRobotsTxt(siteUrl), {
    status: 200,
    headers: { 'Content-Type': CONTENT_TYPE, 'Cache-Control': CACHE_CONTROL },
  });
}

export const GET: APIRoute = ({ request, locals }) => handleRobotsTxtRequest(request, resolveServerEnv(locals));
