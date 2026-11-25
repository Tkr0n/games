/**
 * Page-wide currency store for the React islands.
 *
 * Astro can split every island into its own bundle, so a module-level variable
 * alone is not enough to keep two islands in sync. This store therefore layers
 * three mechanisms:
 *
 * 1. `localStorage` (`verkku_currency`) — the durable preference.
 * 2. A `verkku:currency-change` window event — immediate cross-island sync even
 *    when the bundler duplicates this module.
 * 3. A `storage` listener — syncs other tabs.
 *
 * Exchange rates are public data: they arrive from SSR (`initialRates`) or are
 * fetched lazily from the same-origin BFF (`/api/rates`) only when the visitor
 * picks a crypto currency and no snapshot is available yet.
 */
import { useCallback, useEffect, useSyncExternalStore } from 'react';

import { SUPPORTED_CURRENCIES } from '../../lib/currency';
import type { CurrencyCode, ExchangeRatesDto, ProblemDetailsDto } from '../../lib/types';

export const CURRENCY_STORAGE_KEY = 'verkku_currency';

/** Window event broadcast by every `setCurrency` call. */
export const CURRENCY_CHANGE_EVENT = 'verkku:currency-change';

const DEFAULT_CURRENCY: CurrencyCode = 'USD';
const RATES_ENDPOINT = '/api/rates';

let currentCurrency: CurrencyCode = DEFAULT_CURRENCY;
let currentRates: ExchangeRatesDto | null = null;
let loadingRates = false;
let ratesError: ProblemDetailsDto | null = null;
let ratesPromise: Promise<void> | null = null;
let version = 0;
let hydrated = false;

const listeners = new Set<() => void>();

function emit(): void {
  version += 1;

  for (const listener of listeners) {
    listener();
  }
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);

  return () => {
    listeners.delete(listener);
  };
}

function getSnapshot(): number {
  return version;
}

function getServerSnapshot(): number {
  return 0;
}

export function isCurrencyCode(value: unknown): value is CurrencyCode {
  return typeof value === 'string' && (SUPPORTED_CURRENCIES as readonly string[]).includes(value);
}

function readStoredCurrency(): CurrencyCode {
  try {
    const stored = window.localStorage.getItem(CURRENCY_STORAGE_KEY);
    return isCurrencyCode(stored) ? stored : DEFAULT_CURRENCY;
  } catch {
    return DEFAULT_CURRENCY;
  }
}

function applyCurrency(next: CurrencyCode): void {
  if (next === currentCurrency) return;

  currentCurrency = next;
  emit();
}

function isExchangeRates(value: unknown): value is ExchangeRatesDto {
  if (!value || typeof value !== 'object') return false;

  const candidate = value as Partial<ExchangeRatesDto>;
  const rates = candidate.ratesPerUsd;

  return (
    typeof candidate.retrievedAtUtc === 'string' &&
    typeof rates === 'object' &&
    rates !== null &&
    typeof (rates as Record<string, unknown>).USD === 'number'
  );
}

function toProblemDetails(value: unknown, status: number): ProblemDetailsDto {
  const fallback: ProblemDetailsDto = {
    status,
    code: 'rates_unavailable',
    title: 'Exchange rates unavailable',
  };

  if (!value || typeof value !== 'object') return fallback;

  const candidate = value as Record<string, unknown>;
  const code = typeof candidate.code === 'string' ? candidate.code : fallback.code;
  const title = typeof candidate.title === 'string' ? candidate.title : fallback.title;
  const detail = typeof candidate.detail === 'string' ? candidate.detail : undefined;

  return detail ? { status, code, title, detail } : { status, code, title };
}

export function getCurrency(): CurrencyCode {
  return currentCurrency;
}

export function getExchangeRates(): ExchangeRatesDto | null {
  return currentRates;
}

/**
 * Seeds the shared rate snapshot from SSR. The first non-null snapshot wins, so
 * an island that mounts after a lazy fetch can never downgrade fresh data.
 */
export function hydrateRates(initial?: ExchangeRatesDto | null): void {
  if (!initial || currentRates) return;

  currentRates = initial;
  ratesError = null;
  emit();
}

/** Lazily loads `/api/rates`; concurrent callers share one request. */
export function ensureRates(): Promise<void> {
  if (typeof window === 'undefined') return Promise.resolve();
  if (currentRates) return Promise.resolve();
  if (ratesPromise) return ratesPromise;

  loadingRates = true;
  ratesError = null;
  emit();

  ratesPromise = (async () => {
    try {
      const response = await fetch(RATES_ENDPOINT, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      const payload: unknown = await response.json().catch(() => null);

      if (!response.ok) {
        ratesError = toProblemDetails(payload, response.status);
      } else if (isExchangeRates(payload)) {
        currentRates = payload;
        ratesError = null;
      } else {
        ratesError = toProblemDetails(null, 503);
      }
    } catch {
      ratesError = toProblemDetails(null, 503);
    } finally {
      loadingRates = false;
      ratesPromise = null;
      emit();
    }
  })();

  return ratesPromise;
}

/** Persists the pick and broadcasts it to every island (and other tabs). */
export function setCurrency(next: CurrencyCode): void {
  if (!isCurrencyCode(next)) return;
  if (typeof window === 'undefined') return;

  try {
    window.localStorage.setItem(CURRENCY_STORAGE_KEY, next);
  } catch {
    // Private mode / blocked storage: the in-memory value still works.
  }

  applyCurrency(next);

  try {
    window.dispatchEvent(new CustomEvent<CurrencyCode>(CURRENCY_CHANGE_EVENT, { detail: next }));
  } catch {
    // CustomEvent is unavailable in exotic runtimes; local sync already ran.
  }

  if (next !== 'USD' && !currentRates) {
    void ensureRates();
  }
}

function onCurrencyChange(event: Event): void {
  const detail = (event as CustomEvent<unknown>).detail;
  if (isCurrencyCode(detail)) applyCurrency(detail);
}

function onStorage(event: StorageEvent): void {
  if (event.key !== CURRENCY_STORAGE_KEY) return;
  applyCurrency(isCurrencyCode(event.newValue) ? event.newValue : DEFAULT_CURRENCY);
}

/**
 * Wires the browser-only pieces once per page: stored preference, cross-island
 * events and the cross-tab `storage` listener. Safe to call from every island.
 */
export function initializeCurrencyStore(initialRates?: ExchangeRatesDto | null): void {
  if (typeof window === 'undefined') {
    hydrateRates(initialRates);
    return;
  }

  if (hydrated) {
    hydrateRates(initialRates);
    return;
  }

  hydrated = true;
  hydrateRates(initialRates);
  applyCurrency(readStoredCurrency());

  window.addEventListener(CURRENCY_CHANGE_EVENT, onCurrencyChange);
  window.addEventListener('storage', onStorage);
}

export interface UseCurrencyResult {
  currency: CurrencyCode;
  setCurrency: (next: CurrencyCode) => void;
  rates: ExchangeRatesDto | null;
  loadingRates: boolean;
  ratesError: ProblemDetailsDto | null;
}

/**
 * Reactive view over the store. `useSyncExternalStore` keeps server HTML and
 * the first client render identical (`USD`), then applies the stored value in
 * an effect — no hydration mismatch.
 */
export function useCurrency(initialRates?: ExchangeRatesDto | null): UseCurrencyResult {
  useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);

  useEffect(() => {
    initializeCurrencyStore(initialRates);

    if (getCurrency() !== 'USD' && !getExchangeRates()) {
      void ensureRates();
    }
  }, [initialRates]);

  const update = useCallback((next: CurrencyCode) => {
    setCurrency(next);
  }, []);

  return {
    currency: currentCurrency,
    setCurrency: update,
    rates: currentRates,
    loadingRates,
    ratesError,
  };
}
