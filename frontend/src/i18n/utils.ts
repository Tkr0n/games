import { DEFAULT_LOCALE, LOCALES, type Dictionary, type Locale } from './config';
import { en } from './en';
import { es } from './es';

const DICTIONARIES: Record<Locale, Dictionary> = { es, en };

const LOCALE_PREFIX = /^\/(es|en)(?=\/|$)/;

/** Narrowing guard for anything that arrives as a string (query, cookie, CMS). */
export function isLocale(value?: string): value is Locale {
  return typeof value === 'string' && (LOCALES as readonly string[]).includes(value);
}

export function getDictionary(locale: Locale): Dictionary {
  return DICTIONARIES[locale];
}

/**
 * Swaps the locale segment of a pathname while preserving any query/hash.
 * Prefixes only match whole segments, so `/espanol` is not treated as `/es`.
 */
export function switchLocalePath(pathname: string, targetLocale: Locale): string {
  const raw = pathname ?? '';
  const hashIndex = raw.indexOf('#');
  const hash = hashIndex >= 0 ? raw.slice(hashIndex) : '';
  const withoutHash = hashIndex >= 0 ? raw.slice(0, hashIndex) : raw;
  const queryIndex = withoutHash.indexOf('?');
  const search = queryIndex >= 0 ? withoutHash.slice(queryIndex) : '';
  const path = queryIndex >= 0 ? withoutHash.slice(0, queryIndex) : withoutHash;

  const normalized = path.startsWith('/') ? path : `/${path}`;
  const match = LOCALE_PREFIX.exec(normalized);
  const rest = match ? normalized.slice(match[0].length) : normalized;
  const tail = match || rest !== '/' ? rest : '';

  return `/${targetLocale}${tail}${search}${hash}`;
}

interface ParsedCookie {
  name: string;
  value: string;
}

function parseCookies(cookieHeader: string): ParsedCookie[] {
  return cookieHeader
    .split(';')
    .map((part) => {
      const separator = part.indexOf('=');
      if (separator < 0) return null;

      const name = part.slice(0, separator).trim();
      const value = part.slice(separator + 1).trim();

      if (!name) return null;

      try {
        return { name, value: decodeURIComponent(value) };
      } catch {
        return { name, value };
      }
    })
    .filter((cookie): cookie is ParsedCookie => cookie !== null);
}

interface WeightedLanguage {
  tag: string;
  quality: number;
  index: number;
}

function parseAcceptLanguage(header: string): WeightedLanguage[] {
  return header
    .split(',')
    .map((part, index) => {
      const [tag, ...params] = part.trim().split(';');
      const qualityParam = params.map((param) => param.trim()).find((param) => param.startsWith('q='));
      const quality = qualityParam ? Number.parseFloat(qualityParam.slice(2)) : 1;

      return {
        tag: (tag ?? '').trim().toLowerCase(),
        quality: Number.isFinite(quality) ? quality : 1,
        index,
      };
    })
    .filter((language) => language.tag.length > 0 && language.tag !== '*' && language.quality > 0)
    .sort((a, b) => (b.quality - a.quality) || (a.index - b.index));
}

/**
 * Resolution order: explicit `locale`/`lang` cookie -> Accept-Language -> `es`.
 * Country subtags are collapsed (`en-US` -> `en`, `es-VE` -> `es`).
 */
export function detectPreferredLocale(
  cookieHeader: string | null,
  acceptLanguageHeader: string | null,
): Locale {
  if (cookieHeader) {
    const cookies = parseCookies(cookieHeader);

    for (const name of ['locale', 'lang']) {
      const cookie = cookies.find((candidate) => candidate.name === name);
      if (cookie && isLocale(cookie.value)) return cookie.value;
    }
  }

  if (acceptLanguageHeader) {
    for (const language of parseAcceptLanguage(acceptLanguageHeader)) {
      const primary = language.tag.split('-')[0];
      if (isLocale(primary)) return primary;
    }
  }

  return DEFAULT_LOCALE;
}
