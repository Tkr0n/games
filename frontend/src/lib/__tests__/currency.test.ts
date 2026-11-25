// @ts-ignore -- `bun:test` ships with the Bun runtime; its types are not part of the Astro app tsconfig (see task-4 report).
import { describe, expect, test } from 'bun:test';

import type { Locale } from '../../i18n/config';
import { CURRENCY_DECIMALS, SUPPORTED_CURRENCIES, convertUsdAmount, formatMoneyDisplay } from '../currency';
import type { ExchangeRatesDto } from '../types';

/** Binance-style reference snapshot: 1 USD buys `1 / price` BTC or ETH. */
const BTC_PER_USD = 1 / 96420.15;
const ETH_PER_USD = 1 / 3120.4;

const rates: ExchangeRatesDto = {
  baseCurrency: 'USD',
  ratesPerUsd: {
    USD: 1,
    USDT: 1,
    USDC: 1,
    BTC: BTC_PER_USD,
    ETH: ETH_PER_USD,
  },
  retrievedAtUtc: '2026-11-25T00:00:00Z',
};

const LOCALES: readonly Locale[] = ['es', 'en'];

const hasScientificNotation = (value: string): boolean => /[eE]/.test(value);

describe('supported currencies and decimals', () => {
  test('lists the five supported currencies in canonical order', () => {
    expect(SUPPORTED_CURRENCIES).toEqual(['USD', 'USDT', 'USDC', 'BTC', 'ETH']);
  });

  test('pins decimals per currency', () => {
    expect(CURRENCY_DECIMALS).toEqual({
      USD: 2,
      USDT: 2,
      USDC: 2,
      ETH: 6,
      BTC: 8,
    });
  });
});

describe('convertUsdAmount', () => {
  test('renders USD with two decimals and needs no exchange rates', () => {
    expect(convertUsdAmount(12.5, 'USD', rates)).toBe('12.50');
    expect(convertUsdAmount(0.5, 'USD', null)).toBe('0.50');
    expect(convertUsdAmount(999, 'USDT', rates)).toBe('999.00');
    expect(convertUsdAmount(999, 'USDC', rates)).toBe('999.00');
  });

  test('renders $0.50 -> BTC as 8-decimal fixed point without scientific notation', () => {
    const converted = convertUsdAmount(0.5, 'BTC', rates);

    expect(converted).toBe('0.00000519');
    expect(converted).not.toBeNull();
    expect(hasScientificNotation(converted as string)).toBe(false);
  });

  test('renders ETH with six decimals and never in scientific notation', () => {
    const converted = convertUsdAmount(10, 'ETH', rates);

    expect(converted).toBe('0.003205');
    expect(hasScientificNotation(converted as string)).toBe(false);
  });

  test('keeps trailing zero padding for small crypto amounts', () => {
    expect(convertUsdAmount(0.5, 'ETH', rates)).toBe('0.000160');
    expect(convertUsdAmount(1, 'BTC', rates)).toBe('0.00001037');
  });

  test('returns null for any non-USD currency when rates are missing', () => {
    for (const currency of ['USDT', 'USDC', 'BTC', 'ETH'] as const) {
      expect(convertUsdAmount(0.5, currency, null)).toBeNull();
    }
  });

  test('returns null for non-positive or non-finite rates', () => {
    const invalid = (rate: number): ExchangeRatesDto => ({
      ...rates,
      ratesPerUsd: { ...rates.ratesPerUsd, BTC: rate },
    });

    expect(convertUsdAmount(0.5, 'BTC', invalid(0))).toBeNull();
    expect(convertUsdAmount(0.5, 'BTC', invalid(-1))).toBeNull();
    expect(convertUsdAmount(0.5, 'BTC', invalid(Number.NaN))).toBeNull();
    expect(convertUsdAmount(0.5, 'BTC', invalid(Number.POSITIVE_INFINITY))).toBeNull();
  });

  test('handles zero USD amounts from either base', () => {
    expect(convertUsdAmount(0, 'USD', null)).toBe('0.00');
    expect(convertUsdAmount(0, 'BTC', rates)).toBe('0.00000000');
  });

  test('keeps every supported currency in fixed-point form', () => {
    const amounts = [0.5, 1, 12.34, 96420.15];

    for (const currency of SUPPORTED_CURRENCIES) {
      for (const amount of amounts) {
        const converted = convertUsdAmount(amount, currency, rates);

        expect(converted).not.toBeNull();
        expect(hasScientificNotation(converted as string)).toBe(false);
        expect((converted as string).split('.')[1]?.length).toBe(CURRENCY_DECIMALS[currency]);
      }
    }
  });
});

describe('formatMoneyDisplay', () => {
  test('USD primary and reference both render two decimals', () => {
    for (const locale of LOCALES) {
      expect(formatMoneyDisplay(0.5, 'USD', null, locale)).toEqual({
        primary: '$0.50 USD',
        referenceUsd: '$0.50 USD',
        unavailableRate: false,
      });
    }
  });

  test('crypto prices lead with the converted amount and keep the USD reference', () => {
    for (const locale of LOCALES) {
      expect(formatMoneyDisplay(0.5, 'BTC', rates, locale)).toEqual({
        primary: '0.00000519 BTC',
        referenceUsd: '$0.50 USD',
        unavailableRate: false,
      });
    }
  });

  test('missing rates fall back to USD and flag the unavailable rate', () => {
    for (const currency of ['BTC', 'ETH', 'USDC'] as const) {
      expect(formatMoneyDisplay(0.5, currency, null, 'es')).toEqual({
        primary: '$0.50 USD',
        referenceUsd: '$0.50 USD',
        unavailableRate: true,
      });
    }
  });

  test('flags unavailable rates when the crypto rate is invalid', () => {
    const zeroed: ExchangeRatesDto = {
      ...rates,
      ratesPerUsd: { ...rates.ratesPerUsd, BTC: 0 },
    };

    expect(formatMoneyDisplay(0.5, 'BTC', zeroed, 'en')).toEqual({
      primary: '$0.50 USD',
      referenceUsd: '$0.50 USD',
      unavailableRate: true,
    });
  });

  test('reference USD always pads to two decimals', () => {
    const display = formatMoneyDisplay(7, 'ETH', rates, 'es');

    expect(display.referenceUsd).toBe('$7.00 USD');
    expect(display.primary).toBe('0.002243 ETH');
  });
});
