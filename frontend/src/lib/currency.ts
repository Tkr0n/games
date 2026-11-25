import type { Locale } from '../i18n/config';
import type { CurrencyCode, ExchangeRatesDto } from './types';

export const SUPPORTED_CURRENCIES: readonly CurrencyCode[] = ['USD', 'USDT', 'USDC', 'BTC', 'ETH'];

/**
 * Display precision per currency.
 *
 * Crypto needs enough decimals that a `$0.50` top-up does not collapse to
 * `0.00` — and it must always render fixed-point, never `5.19e-6`.
 */
export const CURRENCY_DECIMALS: Record<CurrencyCode, number> = {
  USD: 2,
  USDT: 2,
  USDC: 2,
  ETH: 6,
  BTC: 8,
};

function resolveRate(currency: CurrencyCode, rates: ExchangeRatesDto | null): number | null {
  const rate = rates?.ratesPerUsd?.[currency];

  if (typeof rate !== 'number' || !Number.isFinite(rate) || rate <= 0) {
    return null;
  }

  return rate;
}

/**
 * Converts a USD amount into `currency` and renders it as a fixed-point string.
 *
 * Returns `null` when a non-USD conversion is impossible (missing, zero,
 * negative, `NaN` or infinite rate). USD never needs a rate: the catalog is
 * priced in USD by contract.
 */
export function convertUsdAmount(
  usdAmount: number,
  currency: CurrencyCode,
  rates: ExchangeRatesDto | null,
): string | null {
  if (currency === 'USD') {
    return usdAmount.toFixed(CURRENCY_DECIMALS.USD);
  }

  const rate = resolveRate(currency, rates);
  if (rate === null) return null;

  return (usdAmount * rate).toFixed(CURRENCY_DECIMALS[currency]);
}

export interface MoneyDisplay {
  primary: string;
  referenceUsd: string;
  unavailableRate: boolean;
}

/**
 * Builds the two-line price label used across cards and product pages.
 *
 * The USD reference is always present so the buyer can audit the crypto
 * conversion. `locale` is accepted for call-site symmetry: crypto amounts stay
 * dot-decimal on purpose so they remain copy/pasteable into wallets, while
 * localized currency words live in the dictionary.
 */
export function formatMoneyDisplay(
  usdAmount: number,
  currency: CurrencyCode,
  rates: ExchangeRatesDto | null,
  locale: Locale,
): MoneyDisplay {
  void locale;

  const referenceUsd = `$${usdAmount.toFixed(CURRENCY_DECIMALS.USD)} USD`;

  if (currency === 'USD') {
    return { primary: referenceUsd, referenceUsd, unavailableRate: false };
  }

  const converted = convertUsdAmount(usdAmount, currency, rates);

  if (converted === null) {
    return { primary: referenceUsd, referenceUsd, unavailableRate: true };
  }

  return { primary: `${converted} ${currency}`, referenceUsd, unavailableRate: false };
}
