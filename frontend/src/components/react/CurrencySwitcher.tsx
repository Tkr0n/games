import { useId } from 'react';

import type { Locale } from '../../i18n/config';
import { getDictionary } from '../../i18n/utils';
import { SUPPORTED_CURRENCIES } from '../../lib/currency';
import type { ExchangeRatesDto } from '../../lib/types';
import { isCurrencyCode, useCurrency } from './CurrencyStore';

interface CurrencySwitcherProps {
  locale: Locale;
  /** Public rate snapshot rendered by SSR; never contains secrets. */
  initialRates?: ExchangeRatesDto | null;
  className?: string;
}

/**
 * Compact, accessible currency picker for the navbar.
 *
 * The `<select>` keeps the control small on mobile while staying keyboard and
 * screen-reader friendly. Every option is a ticker symbol, which stays
 * untranslated on purpose.
 */
export default function CurrencySwitcher({ locale, initialRates, className }: CurrencySwitcherProps) {
  const t = getDictionary(locale);
  const selectId = useId();
  const hintId = useId();
  const { currency, setCurrency, loadingRates, ratesError } = useCurrency(initialRates);

  const hint = ratesError && currency !== 'USD' ? t.currency.unavailableRate : t.currency.usdHint;

  return (
    <div className={`relative inline-flex items-center ${className ?? ''}`}>
      <label htmlFor={selectId} className="sr-only">
        {t.nav.currencyLabel}
      </label>
      <select
        id={selectId}
        value={currency}
        onChange={(event) => {
          const next = event.target.value;
          if (isCurrencyCode(next)) setCurrency(next);
        }}
        aria-describedby={hintId}
        aria-busy={loadingRates}
        className="appearance-none rounded-lg border border-verkku-mint-soft/30 bg-verkku-surface py-1.5 pl-3 pr-8 text-xs font-semibold tracking-wide text-verkku-light transition-colors hover:border-verkku-neon/60 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-verkku-neon"
      >
        {SUPPORTED_CURRENCIES.map((code) => (
          <option key={code} value={code} className="bg-verkku-surface text-verkku-light">
            {code}
          </option>
        ))}
      </select>
      <span
        aria-hidden="true"
        className="pointer-events-none absolute right-2.5 text-[10px] leading-none text-verkku-mint-soft"
      >
        ▾
      </span>
      <span id={hintId} className="sr-only">
        {hint}
      </span>
    </div>
  );
}
