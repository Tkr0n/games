import type { Locale } from '../i18n/config';
import type { CatalogItemDetailDto } from './types';

export const PLACEHOLDER_PRODUCT_IMAGE = '/assets/placeholder-product.svg';

export const SITE_NAME = 'Verkku Games';

const SITE_DESCRIPTION =
  'Verkku Games — top-ups and gift cards for your favorite games, with live USD prices.';

const LOCALE_PREFIX = /^\/(es|en)(?=\/|$)/;

/** The wholesale provider name must never reach public output (Review Focus #4). */
const PROVIDER_NAME_PATTERN = /shop\s*2\s*top\s*up/gi;

function scrubBrandCopy(value: string): string {
  return value.replace(PROVIDER_NAME_PATTERN, SITE_NAME).replace(/\s+/g, ' ').trim();
}

/**
 * Only absolute `https:` URLs pass. Everything else — `http:`, `javascript:`,
 * `data:`, protocol-relative, blank or malformed — collapses to the local
 * placeholder (Review Focus #3).
 */
export function sanitizeExternalImageUrl(url?: string | null): string {
  if (typeof url !== 'string') return PLACEHOLDER_PRODUCT_IMAGE;

  const trimmed = url.trim();
  if (!trimmed) return PLACEHOLDER_PRODUCT_IMAGE;

  let parsed: URL;
  try {
    parsed = new URL(trimmed);
  } catch {
    return PLACEHOLDER_PRODUCT_IMAGE;
  }

  if (parsed.protocol !== 'https:' || !parsed.hostname) {
    return PLACEHOLDER_PRODUCT_IMAGE;
  }

  return parsed.toString();
}

export function normalizeSiteUrl(siteUrl: string): string {
  return (siteUrl ?? '').trim().replace(/\/+$/, '');
}

export function buildCanonicalUrl(siteUrl: string, pathname: string): string {
  const base = normalizeSiteUrl(siteUrl);
  const path = (pathname ?? '').trim();

  if (!path) return base;

  return `${base}${path.startsWith('/') ? path : `/${path}`}`;
}

/** Drops the locale segment so one path can be re-emitted per locale. */
function stripLocalePrefix(pathname: string): string {
  const path = (pathname ?? '').trim().split(/[?#]/)[0] ?? '';
  if (path === '' || path === '/') return '';

  const stripped = path.replace(LOCALE_PREFIX, '');

  if (stripped === '' || stripped === '/') return '';
  return stripped.startsWith('/') ? stripped : `/${stripped}`;
}

function localePath(siteUrl: string, locale: Locale, pathname: string): string {
  return `${normalizeSiteUrl(siteUrl)}/${locale}${stripLocalePrefix(pathname)}`;
}

export function buildHreflangLinks(
  siteUrl: string,
  pathname: string,
): Array<{ hreflang: 'es' | 'en' | 'x-default'; href: string }> {
  const esHref = localePath(siteUrl, 'es', pathname);
  const enHref = localePath(siteUrl, 'en', pathname);

  return [
    { hreflang: 'es', href: esHref },
    { hreflang: 'en', href: enHref },
    { hreflang: 'x-default', href: esHref },
  ];
}

export function buildOrganizationJsonLd(siteUrl: string): Record<string, unknown> {
  const base = normalizeSiteUrl(siteUrl);

  return {
    '@context': 'https://schema.org',
    '@type': 'Organization',
    name: SITE_NAME,
    url: base,
    logo: `${base}/assets/logo.svg`,
    description: SITE_DESCRIPTION,
  };
}

export function buildWebSiteJsonLd(siteUrl: string, locale: Locale): Record<string, unknown> {
  const base = normalizeSiteUrl(siteUrl);

  return {
    '@context': 'https://schema.org',
    '@type': 'WebSite',
    name: SITE_NAME,
    url: `${base}/${locale}`,
    inLanguage: locale,
    potentialAction: {
      '@type': 'SearchAction',
      target: {
        '@type': 'EntryPoint',
        urlTemplate: `${base}/${locale}/catalog?q={search_term_string}`,
      },
      'query-input': 'required name=search_term_string',
    },
  };
}

export function buildBreadcrumbJsonLd(
  siteUrl: string,
  items: Array<{ name: string; path: string }>,
): Record<string, unknown> {
  return {
    '@context': 'https://schema.org',
    '@type': 'BreadcrumbList',
    itemListElement: items.map((entry, index) => ({
      '@type': 'ListItem',
      position: index + 1,
      name: scrubBrandCopy(entry.name),
      item: buildCanonicalUrl(siteUrl, entry.path),
    })),
  };
}

export function buildProductJsonLd(
  siteUrl: string,
  locale: Locale,
  detail: CatalogItemDetailDto,
): Record<string, unknown> {
  const item = detail.item;
  const base = normalizeSiteUrl(siteUrl);
  const productUrl = `${base}/${locale}/product/${encodeURIComponent(item.providerUid)}/${encodeURIComponent(item.externalId)}`;

  const offer: Record<string, unknown> = {
    '@type': 'Offer',
    priceCurrency: 'USD',
    availability: item.inStock ? 'https://schema.org/InStock' : 'https://schema.org/OutOfStock',
    url: productUrl,
    itemCondition: 'https://schema.org/NewCondition',
  };

  if (item.retailPriceUsd && Number.isFinite(item.retailPriceUsd.amount)) {
    offer.price = item.retailPriceUsd.amount.toFixed(2);
  }

  return {
    '@context': 'https://schema.org',
    '@type': 'Product',
    name: scrubBrandCopy(item.name),
    description: scrubBrandCopy(item.description),
    image: sanitizeExternalImageUrl(item.imageUrl),
    sku: item.externalId,
    category: scrubBrandCopy(item.categoryName),
    url: productUrl,
    brand: {
      '@type': 'Brand',
      name: SITE_NAME,
    },
    offers: offer,
  };
}
