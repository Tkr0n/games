// @ts-ignore -- `bun:test` ships with the Bun runtime; its types are not part of the Astro app tsconfig (see task-4 report).
import { describe, expect, test } from 'bun:test';

import {
  PLACEHOLDER_PRODUCT_IMAGE,
  buildBreadcrumbJsonLd,
  buildCanonicalUrl,
  buildHreflangLinks,
  buildOrganizationJsonLd,
  buildProductJsonLd,
  buildWebSiteJsonLd,
  normalizeSiteUrl,
  sanitizeExternalImageUrl,
} from '../seo';
import type { CatalogItemDetailDto, CatalogItemDto } from '../types';

const SITE = 'https://games.verkku.com';

const baseItem: CatalogItemDto = {
  providerUid: 'd4f8a2c1-7b3e-4f90-9a12-6e8b5c3d1f42',
  externalId: 'item-123',
  categoryId: 'cat-1',
  categoryName: 'Roblox',
  name: 'Roblox 1,000 Robux',
  description: 'Carga instantánea de Robux en tu cuenta.',
  imageUrl: 'https://cdn.example.com/game.webp',
  productType: 'topup',
  region: 'USA',
  retailPriceUsd: { amount: 9.99, currency: 'USD' },
  inStock: true,
  pricesLocked: false,
};

const detail: CatalogItemDetailDto = {
  item: baseItem,
  requirements: [
    {
      fieldName: 'playerId',
      dataType: 'text',
      placeholder: 'Tu ID de jugador',
      selectOptions: [],
      minLength: 3,
      maxLength: 40,
    },
  ],
  siblingItems: [],
  relatedRegionCategories: [],
};

const parseJsonLd = (value: unknown): Record<string, unknown> => JSON.parse(JSON.stringify(value)) as Record<string, unknown>;

describe('sanitizeExternalImageUrl', () => {
  test('accepts valid https URLs', () => {
    expect(sanitizeExternalImageUrl('https://cdn.example.com/game.webp')).toBe('https://cdn.example.com/game.webp');
    expect(sanitizeExternalImageUrl('  https://cdn.example.com/game.webp  ')).toBe('https://cdn.example.com/game.webp');
    expect(sanitizeExternalImageUrl('https://cdn.example.com/game.png?v=2')).toBe('https://cdn.example.com/game.png?v=2');
  });

  test('rejects javascript, http, data and malformed values (Review Focus #3)', () => {
    const rejected = [
      'javascript:alert(1)',
      'JavaScript:alert(1)',
      'http://example.com/a.png',
      'data:image/svg+xml;base64,PHN2Zy8+',
      '',
      '   ',
      'not a url',
      '//example.com/a.png',
      'ftp://example.com/a.png',
      null,
      undefined,
    ];

    for (const value of rejected) {
      expect(sanitizeExternalImageUrl(value)).toBe(PLACEHOLDER_PRODUCT_IMAGE);
    }
  });

  test('exposes the local placeholder asset', () => {
    expect(PLACEHOLDER_PRODUCT_IMAGE).toBe('/assets/placeholder-product.svg');
  });
});

describe('URL helpers', () => {
  test('normalizes site URLs without a trailing slash', () => {
    expect(normalizeSiteUrl(`${SITE}/`)).toBe(SITE);
    expect(normalizeSiteUrl(`${SITE}///`)).toBe(SITE);
    expect(normalizeSiteUrl(`  ${SITE}/es/  `)).toBe(`${SITE}/es`);
    expect(normalizeSiteUrl('https://games.verkku.com')).toBe(SITE);
  });

  test('builds canonical URLs from site + pathname', () => {
    expect(buildCanonicalUrl(SITE, '/es/catalogo')).toBe(`${SITE}/es/catalogo`);
    expect(buildCanonicalUrl(`${SITE}/`, 'es/catalogo')).toBe(`${SITE}/es/catalogo`);
    expect(buildCanonicalUrl(SITE, '')).toBe(SITE);
    expect(buildCanonicalUrl(SITE, '/')).toBe(`${SITE}/`);
  });

  test('builds es/en/x-default hreflang alternates', () => {
    const links = buildHreflangLinks(SITE, '/es/catalogo');

    expect(links).toEqual([
      { hreflang: 'es', href: `${SITE}/es/catalogo` },
      { hreflang: 'en', href: `${SITE}/en/catalogo` },
      { hreflang: 'x-default', href: `${SITE}/es/catalogo` },
    ]);

    expect(buildHreflangLinks(SITE, '/catalogo')).toEqual(links);
    expect(buildHreflangLinks(SITE, '/')).toEqual([
      { hreflang: 'es', href: `${SITE}/es` },
      { hreflang: 'en', href: `${SITE}/en` },
      { hreflang: 'x-default', href: `${SITE}/es` },
    ]);
  });
});

describe('JSON-LD builders', () => {
  test('organization JSON-LD carries brand identity and no provider leakage', () => {
    const jsonLd = parseJsonLd(buildOrganizationJsonLd(SITE));

    expect(jsonLd['@context']).toBe('https://schema.org');
    expect(jsonLd['@type']).toBe('Organization');
    expect(jsonLd.name).toBe('Verkku Games');
    expect(jsonLd.url).toBe(SITE);
    expect(String(jsonLd.logo)).toContain('/assets/logo.svg');
    expect(JSON.stringify(jsonLd).toLowerCase()).not.toContain('shop2topup');
  });

  test('website JSON-LD declares the active locale', () => {
    const es = parseJsonLd(buildWebSiteJsonLd(SITE, 'es'));
    const en = parseJsonLd(buildWebSiteJsonLd(SITE, 'en'));

    expect(es['@type']).toBe('WebSite');
    expect(es.inLanguage).toBe('es');
    expect(es.url).toBe(`${SITE}/es`);
    expect(en.inLanguage).toBe('en');
    expect(en.url).toBe(`${SITE}/en`);
    expect(es.name).toBe('Verkku Games');
  });

  test('breadcrumb JSON-LD positions its items from one', () => {
    const jsonLd = parseJsonLd(
      buildBreadcrumbJsonLd(SITE, [
        { name: 'Inicio', path: '/es' },
        { name: 'Catálogo', path: '/es/catalogo' },
      ]),
    );

    expect(jsonLd['@type']).toBe('BreadcrumbList');
    expect(jsonLd.itemListElement).toEqual([
      { '@type': 'ListItem', position: 1, name: 'Inicio', item: `${SITE}/es` },
      { '@type': 'ListItem', position: 2, name: 'Catálogo', item: `${SITE}/es/catalogo` },
    ]);
  });

  test('product JSON-LD emits a valid Product + Offer in stock', () => {
    const jsonLd = parseJsonLd(buildProductJsonLd(SITE, 'es', detail));

    expect(jsonLd['@context']).toBe('https://schema.org');
    expect(jsonLd['@type']).toBe('Product');
    expect(jsonLd.name).toBe(baseItem.name);
    expect(jsonLd.image).toBe('https://cdn.example.com/game.webp');

    const offers = jsonLd.offers as Record<string, unknown>;
    expect(offers['@type']).toBe('Offer');
    expect(offers.priceCurrency).toBe('USD');
    expect(offers.price).toBe('9.99');
    expect(offers.availability).toBe('https://schema.org/InStock');
    expect(JSON.stringify(jsonLd).toLowerCase()).not.toContain('shop2topup');
  });

  test('product JSON-LD marks unavailable products as OutOfStock', () => {
    const outOfStock: CatalogItemDetailDto = {
      ...detail,
      item: { ...baseItem, inStock: false, retailPriceUsd: null },
    };
    const jsonLd = parseJsonLd(buildProductJsonLd(SITE, 'en', outOfStock));
    const offers = jsonLd.offers as Record<string, unknown>;

    expect(offers.availability).toBe('https://schema.org/OutOfStock');
    expect(offers.priceCurrency).toBe('USD');
    expect(offers.price === undefined || typeof offers.price === 'string').toBe(true);
  });

  test('product JSON-LD falls back to the local placeholder for unsafe images', () => {
    const unsafe: CatalogItemDetailDto = {
      ...detail,
      item: { ...baseItem, imageUrl: 'javascript:alert(1)' },
    };
    const jsonLd = parseJsonLd(buildProductJsonLd(SITE, 'es', unsafe));

    expect(jsonLd.image).toBe(PLACEHOLDER_PRODUCT_IMAGE);
    expect(JSON.stringify(jsonLd)).not.toContain('javascript:');
  });

  test('product JSON-LD never leaks the wholesale provider name, even from tainted copy', () => {
    const tainted: CatalogItemDetailDto = {
      ...detail,
      item: {
        ...baseItem,
        name: 'Roblox 1,000 Robux (shop2topup)',
        description: 'SHOP2TOPUP wholesale feed: recarga tus Robux al instante.',
      },
    };

    const serialized = JSON.stringify(buildProductJsonLd(SITE, 'es', tainted)).toLowerCase();
    expect(serialized).not.toContain('shop2topup');
  });
});
