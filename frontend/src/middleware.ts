import { defineMiddleware } from 'astro:middleware';

/**
 * CSP rationale:
 * - `script-src 'self' 'unsafe-inline'`: Astro hydrates islands with inline
 *   bootstrap scripts; no remote script origins are allowed.
 * - `style-src` keeps Google Fonts styles allowed while font binaries come from
 *   `fonts.gstatic.com` only.
 * - `img-src https: data:` permits provider product thumbnails (sanitized to
 *   `https:` on the server) and inline SVG placeholders.
 * - `frame-ancestors 'none'` + `X-Frame-Options: DENY` block clickjacking.
 */
const CONTENT_SECURITY_POLICY = [
  "default-src 'self'",
  "script-src 'self' 'unsafe-inline'",
  "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
  "font-src 'self' https://fonts.gstatic.com",
  "img-src 'self' https: data:",
  "connect-src 'self'",
  "frame-ancestors 'none'",
  "base-uri 'self'",
  "form-action 'self'",
  "object-src 'none'",
  'upgrade-insecure-requests',
].join('; ');

export const SECURITY_HEADERS: Readonly<Record<string, string>> = {
  'Content-Security-Policy': CONTENT_SECURITY_POLICY,
  'Strict-Transport-Security': 'max-age=63072000; includeSubDomains; preload',
  'X-Content-Type-Options': 'nosniff',
  'X-Frame-Options': 'DENY',
  'Referrer-Policy': 'strict-origin-when-cross-origin',
  'Permissions-Policy': 'camera=(), microphone=(), geolocation=(), payment=()',
};

export const onRequest = defineMiddleware(async (_context, next) => {
  const response = await next();

  for (const [name, value] of Object.entries(SECURITY_HEADERS)) {
    response.headers.set(name, value);
  }

  return response;
});
