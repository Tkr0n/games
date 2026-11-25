import type { Locale } from '../../i18n/config';
import { getDictionary } from '../../i18n/utils';
import { formatMoneyDisplay } from '../../lib/currency';
import type { ExchangeRatesDto } from '../../lib/types';
import { useCurrency } from './CurrencyStore';

type PriceSize = 'sm' | 'md' | 'lg';

interface PriceDisplayProps {
  locale: Locale;
  /** Retail price in USD as published by the API; `null` when locked. */
  amountUsd: number | null;
  pricesLocked?: boolean;
  /** Public rate snapshot rendered by SSR; never contains secrets. */
  initialRates?: ExchangeRatesDto | null;
  /** Renders the localized "From" eyebrow above the amount. */
  showFrom?: boolean;
  size?: PriceSize;
  className?: string;
}

const PRIMARY_CLASSES: Record<PriceSize, string> = {
  sm: 'text-sm font-semibold',
  md: 'text-lg font-bold',
  lg: 'text-2xl font-bold sm:text-3xl',
};

/**
 * Renders the selected currency plus the USD reference.
 *
 * A locked price is never turned into a number: it stays a distinct, labeled
 * state so the UI cannot imply a price we do not actually have.
 */
export default function PriceDisplay({
  locale,
  amountUsd,
  pricesLocked = false,
  initialRates,
  showFrom = false,
  size = 'md',
  className,
}: PriceDisplayProps) {
  const t = getDictionary(locale);
  const { currency, rates, loadingRates } = useCurrency(initialRates);

  if (pricesLocked || amountUsd === null) {
    return (
      <div className={`flex flex-col gap-0.5 ${className ?? ''}`}>
        <span className="inline-flex w-fit items-center gap-1 rounded-full border border-verkku-mint-soft/30 bg-verkku-surface px-2 py-0.5 text-[11px] font-semibold text-verkku-mint-soft">
          {t.catalog.card.pricesLocked}
        </span>
      </div>
    );
  }

  const display = formatMoneyDisplay(amountUsd, currency, rates, locale);
  const isCrypto = currency !== 'USD';

  return (
    <div className={`flex flex-col gap-0.5 ${className ?? ''}`}>
      {showFrom ? (
        <span className="text-[11px] font-medium uppercase tracking-wide text-verkku-mint-soft">
          {t.catalog.card.fromPrice}
        </span>
      ) : null}

      <span
        className={`${PRIMARY_CLASSES[size]} text-verkku-light tabular-nums`}
        aria-busy={loadingRates && isCrypto}
      >
        {display.primary}
      </span>

      {isCrypto && !display.unavailableRate ? (
        <span className="text-[11px] leading-snug text-verkku-mint-soft">
          {t.currency.referenceLabel}: <span className="tabular-nums">{display.referenceUsd}</span>
        </span>
      ) : null}

      {display.unavailableRate ? (
        <span className="max-w-[22ch] text-[11px] leading-snug text-verkku-mint-soft">
          {t.currency.unavailableRate}
        </span>
      ) : null}
    </div>
  );
}
