// @ts-ignore -- `bun:test` ships with the Bun runtime; its types are not part of the Astro app tsconfig (see task-4 report).
import { describe, expect, test } from 'bun:test';

import { DEFAULT_LOCALE, LOCALES } from '../config';
import { en } from '../en';
import { es } from '../es';
import { detectPreferredLocale, getDictionary, isLocale, switchLocalePath } from '../utils';
import { ALL_REGIONS, PRIMARY_REGIONS, getRegionMeta, isValidRegionCode } from '../../lib/regions';

/** Collects every leaf key path so ES/EN parity is enforced structurally. */
const flattenKeys = (value: Record<string, unknown>, prefix = ''): string[] => {
  const keys: string[] = [];

  for (const [key, child] of Object.entries(value)) {
    const path = prefix ? `${prefix}.${key}` : key;

    if (child !== null && typeof child === 'object') {
      keys.push(...flattenKeys(child as Record<string, unknown>, path));
    } else {
      keys.push(path);
    }
  }

  return keys.sort();
};

const getCopy = (value: Record<string, unknown>, path: string): unknown =>
  path.split('.').reduce<unknown>((node, segment) => (node as Record<string, unknown> | undefined)?.[segment], value);

describe('locale configuration', () => {
  test('pins the supported locales and the ES default', () => {
    expect(LOCALES).toEqual(['es', 'en']);
    expect(DEFAULT_LOCALE).toBe('es');
  });

  test('isLocale narrows known locales only', () => {
    expect(isLocale('es')).toBe(true);
    expect(isLocale('en')).toBe(true);
    expect(isLocale('fr')).toBe(false);
    expect(isLocale('ES')).toBe(false);
    expect(isLocale(undefined)).toBe(false);
    expect(isLocale('')).toBe(false);
  });
});

describe('dictionary parity', () => {
  test('ES and EN expose identical key sets', () => {
    const esKeys = flattenKeys(es as unknown as Record<string, unknown>);
    const enKeys = flattenKeys(en as unknown as Record<string, unknown>);

    expect(esKeys.length).toBeGreaterThan(60);
    expect(enKeys).toEqual(esKeys);
  });

  test('every leaf is a non-empty string in both locales', () => {
    for (const dictionary of [es, en]) {
      const record = dictionary as unknown as Record<string, unknown>;

      for (const key of flattenKeys(record)) {
        const value = getCopy(record, key);
        expect(typeof value).toBe('string');
        expect((value as string).trim().length).toBeGreaterThan(0);
      }
    }
  });

  test('covers the required product surfaces', () => {
    const required = [
      'nav.home',
      'nav.catalog',
      'nav.studio',
      'hero.title',
      'regions.title',
      'regions.whyBody',
      'currency.title',
      'catalog.filters.searchPlaceholder',
      'catalog.filters.includeGlobal',
      'catalog.filters.productTypeTopup',
      'catalog.filters.productTypeVoucher',
      'catalog.filters.inStockOnly',
      'catalog.pagination.next',
      'product.livePriceTitle',
      'product.refreshPrice',
      'product.pricesLockedBody',
      'product.requirementsTitle',
      'product.siblingTitle',
      'product.crossRegionTitle',
      'product.comingSoonBadge',
      'product.comingSoonBody',
      'errors.apiUnavailableTitle',
      'errors.notFoundTitle',
      'studio.title',
      'footer.rights',
    ];

    for (const path of required) {
      expect(getCopy(es as unknown as Record<string, unknown>, path)).toBeString();
      expect(getCopy(en as unknown as Record<string, unknown>, path)).toBeString();
    }
  });

  test('uses natural localized labels for navigation', () => {
    expect(es.nav.home).toBe('Inicio');
    expect(es.nav.catalog).toBe('Catálogo');
    expect(es.nav.studio).toBe('Studio');
    expect(en.nav.home).toBe('Home');
    expect(en.nav.catalog).toBe('Catalog');
    expect(en.nav.studio).toBe('Studio');
  });

  test('getDictionary returns the dictionary for the requested locale', () => {
    expect(getDictionary('es')).toBe(es);
    expect(getDictionary('en')).toBe(en);
  });
});

describe('switchLocalePath', () => {
  test('swaps the locale prefix (plan pin)', () => {
    expect(switchLocalePath('/es/catalog', 'en')).toBe('/en/catalog');
    expect(switchLocalePath('/en/catalog', 'es')).toBe('/es/catalog');
  });

  test('handles roots and missing prefixes', () => {
    expect(switchLocalePath('/es', 'en')).toBe('/en');
    expect(switchLocalePath('/en', 'es')).toBe('/es');
    expect(switchLocalePath('/es/', 'en')).toBe('/en/');
    expect(switchLocalePath('/', 'en')).toBe('/en');
    expect(switchLocalePath('', 'es')).toBe('/es');
    expect(switchLocalePath('/catalog', 'es')).toBe('/es/catalog');
  });

  test('preserves query strings and hashes', () => {
    expect(switchLocalePath('/es/catalogo?page=2&region=USA#lista', 'en')).toBe('/en/catalogo?page=2&region=USA#lista');
    expect(switchLocalePath('/en?lang=es', 'es')).toBe('/es?lang=es');
  });

  test('never mangles paths that merely start with a locale-like word', () => {
    expect(switchLocalePath('/espanol', 'en')).toBe('/en/espanol');
    expect(switchLocalePath('/english', 'es')).toBe('/es/english');
    expect(switchLocalePath('/es/catalog', 'es')).toBe('/es/catalog');
  });
});

describe('detectPreferredLocale', () => {
  test('defaults to ES without hints', () => {
    expect(detectPreferredLocale(null, null)).toBe('es');
    expect(detectPreferredLocale('', '')).toBe('es');
    expect(detectPreferredLocale(null, 'fr-FR,fr;q=0.9')).toBe('es');
  });

  test('respects accept-language (plan pin)', () => {
    expect(detectPreferredLocale(null, 'en-US,en;q=0.9')).toBe('en');
    expect(detectPreferredLocale(null, 'en')).toBe('en');
    expect(detectPreferredLocale(null, 'es-VE,es;q=0.9,en;q=0.8')).toBe('es');
  });

  test('a locale cookie overrides accept-language (plan pin)', () => {
    expect(detectPreferredLocale('locale=en', 'es-VE,es;q=0.9')).toBe('en');
    expect(detectPreferredLocale('theme=dark; locale=en; session=abc', 'es-VE,es;q=0.9')).toBe('en');
    expect(detectPreferredLocale('locale=es', 'en-US,en;q=0.9')).toBe('es');
    expect(detectPreferredLocale('lang=en', 'es-VE')).toBe('en');
  });

  test('an invalid cookie value falls back to accept-language', () => {
    expect(detectPreferredLocale('locale=fr', 'en-US,en;q=0.9')).toBe('en');
    expect(detectPreferredLocale('locale=fr', 'fr-FR')).toBe('es');
  });
});

describe('region metadata', () => {
  test('pins primary and full region lists', () => {
    expect(PRIMARY_REGIONS).toEqual(['USA', 'GLOBAL', 'MX', 'PA', 'CO']);
    expect(ALL_REGIONS).toEqual(['USA', 'GLOBAL', 'MX', 'PA', 'CO', 'OTHER']);
  });

  test('isValidRegionCode narrows supported regions only', () => {
    for (const region of ALL_REGIONS) {
      expect(isValidRegionCode(region)).toBe(true);
    }

    expect(isValidRegionCode('usa')).toBe(false);
    expect(isValidRegionCode('VE')).toBe(false);
    expect(isValidRegionCode('')).toBe(false);
  });

  test('getRegionMeta localizes labels and keeps stable codes', () => {
    const esMeta = getRegionMeta('USA', 'es');
    const enMeta = getRegionMeta('USA', 'en');

    expect(esMeta.code).toBe('USA');
    expect(esMeta.label).toBe('Estados Unidos');
    expect(enMeta.label).toBe('United States');
    expect(esMeta.shortLabel).not.toBe('');
    expect(esMeta.badgeTone).not.toBe('');
    expect(esMeta.description).not.toBe(enMeta.description);
  });

  test('every region resolves in both locales', () => {
    for (const locale of LOCALES) {
      for (const region of ALL_REGIONS) {
        const meta = getRegionMeta(region, locale);

        expect(meta.code).toBe(region);
        expect(meta.label.trim().length).toBeGreaterThan(0);
        expect(meta.shortLabel.trim().length).toBeGreaterThan(0);
        expect(meta.badgeTone.trim().length).toBeGreaterThan(0);
        expect(meta.description.trim().length).toBeGreaterThan(0);
      }
    }
  });
});
