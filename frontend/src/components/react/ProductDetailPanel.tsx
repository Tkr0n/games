import { useEffect, useId, useRef, useState } from 'react';

import type { Locale } from '../../i18n/config';
import { getDictionary } from '../../i18n/utils';
import { getRegionMeta } from '../../lib/regions';
import { sanitizeExternalImageUrl } from '../../lib/seo';
import type {
  CatalogItemDetailDto,
  ExchangeRatesDto,
  ItemPriceQuoteDto,
  ProblemDetailsDto,
} from '../../lib/types';
import PriceDisplay from './PriceDisplay';

interface ProductDetailPanelProps {
  locale: Locale;
  detail: CatalogItemDetailDto;
  /** Public rate snapshot rendered by SSR; never contains secrets. */
  initialRates: ExchangeRatesDto | null;
}

const ERROR_CODE_PATTERN = /^[a-z0-9_.-]{1,64}$/i;
const MAX_RETRY_AFTER_SECONDS = 86_400;

const REGION_BADGE_CLASSES: Record<string, string> = {
  emerald: 'border-verkku-neon/40 bg-verkku-neon/10 text-verkku-mint',
  mint: 'border-verkku-mint/40 bg-verkku-mint/10 text-verkku-mint',
  teal: 'border-verkku-teal-bright/50 bg-verkku-teal/30 text-verkku-mint-soft',
  'mint-soft': 'border-verkku-mint-soft/40 bg-verkku-mint-soft/10 text-verkku-mint-soft',
  'teal-bright': 'border-verkku-teal-bright/50 bg-verkku-teal-bright/10 text-verkku-mint-soft',
  stone: 'border-verkku-stone/30 bg-verkku-stone/10 text-verkku-stone',
};

function isPriceQuote(value: unknown): value is ItemPriceQuoteDto {
  if (!value || typeof value !== 'object') return false;

  const candidate = value as Partial<ItemPriceQuoteDto>;
  const price = candidate.retailPriceUsd;

  return (
    typeof candidate.quotedAtUtc === 'string' &&
    typeof candidate.inStock === 'boolean' &&
    !!price &&
    typeof price === 'object' &&
    typeof price.amount === 'number' &&
    Number.isFinite(price.amount)
  );
}

function readRetryAfter(value: unknown): number | undefined {
  if (!value || typeof value !== 'object') return undefined;

  const raw = (value as Record<string, unknown>).retryAfterSeconds;
  if (typeof raw !== 'number' || !Number.isInteger(raw)) return undefined;
  if (raw <= 0 || raw > MAX_RETRY_AFTER_SECONDS) return undefined;

  return raw;
}

function toProblem(value: unknown, status: number): ProblemDetailsDto {
  const fallback: ProblemDetailsDto = {
    status,
    code: 'price_unavailable',
    title: 'Price unavailable',
  };

  if (!value || typeof value !== 'object') return fallback;

  const candidate = value as Record<string, unknown>;
  const code = typeof candidate.code === 'string' && ERROR_CODE_PATTERN.test(candidate.code)
    ? candidate.code
    : fallback.code;
  const title = typeof candidate.title === 'string' ? candidate.title : fallback.title;
  const retryAfterSeconds = readRetryAfter(value);

  return retryAfterSeconds ? { status, code, title, retryAfterSeconds } : { status, code, title };
}

function formatTimestamp(value: string, locale: Locale): string | null {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return null;

  try {
    return new Intl.DateTimeFormat(locale, { dateStyle: 'short', timeStyle: 'short' }).format(date);
  } catch {
    return date.toISOString();
  }
}

/**
 * Read-only v1 product panel.
 *
 * It can refresh the live price through the BFF, shows region/stock/requirements
 * and the "Coming soon" notice. There is deliberately no cart, checkout or
 * off-platform sales link.
 */
export default function ProductDetailPanel({ locale, detail, initialRates }: ProductDetailPanelProps) {
  const t = getDictionary(locale);
  const { item, requirements, siblingItems, relatedRegionCategories } = detail;

  const [quote, setQuote] = useState<ItemPriceQuoteDto | null>(null);
  const [checking, setChecking] = useState(false);
  const [priceError, setPriceError] = useState<ProblemDetailsDto | null>(null);
  const [cooldownUntil, setCooldownUntil] = useState<number | null>(null);
  const [now, setNow] = useState(() => Date.now());

  const abortRef = useRef<AbortController | null>(null);
  const livePriceId = useId();
  const requirementsId = useId();

  useEffect(() => {
    if (cooldownUntil === null) return;

    const timer = setInterval(() => {
      const current = Date.now();
      setNow(current);

      if (current >= cooldownUntil) setCooldownUntil(null);
    }, 1000);

    return () => clearInterval(timer);
  }, [cooldownUntil]);

  useEffect(() => {
    return () => abortRef.current?.abort();
  }, []);

  const cooldownRemaining =
    cooldownUntil === null ? 0 : Math.max(0, Math.ceil((cooldownUntil - now) / 1000));
  const coolingDown = cooldownRemaining > 0;

  const checkPrice = (): void => {
    if (checking || coolingDown) return;

    abortRef.current?.abort();

    const controller = new AbortController();
    abortRef.current = controller;

    setChecking(true);
    setPriceError(null);

    const query = new URLSearchParams({ providerUid: item.providerUid, itemId: item.externalId });

    void (async () => {
      try {
        const response = await fetch(`/api/price?${query.toString()}`, {
          method: 'GET',
          headers: { Accept: 'application/json' },
          signal: controller.signal,
        });

        const payload: unknown = await response.json().catch(() => null);

        if (controller.signal.aborted) return;

        if (!response.ok) {
          const problem = toProblem(payload, response.status);
          setPriceError(problem);

          if (problem.retryAfterSeconds) {
            setCooldownUntil(Date.now() + problem.retryAfterSeconds * 1000);
          }

          return;
        }

        if (isPriceQuote(payload)) {
          setQuote(payload);
          setPriceError(null);
        } else {
          setPriceError(toProblem(null, 503));
        }
      } catch (caught) {
        if (controller.signal.aborted) return;
        if (caught instanceof Error && caught.name === 'AbortError') return;

        setPriceError(toProblem(null, 503));
      } finally {
        if (!controller.signal.aborted) setChecking(false);
      }
    })();
  };

  const regionMeta = getRegionMeta(item.region, locale);
  const regionClasses = REGION_BADGE_CLASSES[regionMeta.badgeTone] ?? REGION_BADGE_CLASSES.stone;
  const typeLabel =
    item.productType === 'topup' ? t.product.productTypeTopup : t.product.productTypeVoucher;
  const inStock = quote ? quote.inStock : item.inStock;
  const effectivePrice = quote?.retailPriceUsd.amount ?? item.retailPriceUsd?.amount ?? null;
  const priceLocked = !quote && item.pricesLocked;
  const pricesLockedError = priceError?.code === 'prices_locked';
  const quotedAt = quote ? formatTimestamp(quote.quotedAtUtc, locale) : null;

  return (
    <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.15fr)] lg:items-start">
      <div className="verkku-surface overflow-hidden">
        <div className="bg-verkku-bg-deep p-4">
          <img
            src={sanitizeExternalImageUrl(item.imageUrl)}
            alt={item.name}
            loading="lazy"
            decoding="async"
            referrerPolicy="no-referrer"
            width="720"
            height="540"
            className="aspect-[4/3] w-full object-contain"
          />
        </div>
        <div className="flex flex-wrap gap-1.5 p-4">
          <span className={`rounded-full border px-2.5 py-1 text-[11px] font-semibold ${regionClasses}`}>
            {t.product.regionLabel}: {regionMeta.label}
          </span>
          <span className="rounded-full border border-verkku-mint-soft/35 bg-verkku-bg-deep/60 px-2.5 py-1 text-[11px] font-semibold text-verkku-stone">
            {typeLabel}
          </span>
          <span className="rounded-full border border-verkku-mint-soft/35 bg-verkku-bg-deep/60 px-2.5 py-1 text-[11px] font-semibold text-verkku-stone">
            {inStock ? t.product.inStock : t.product.outOfStock}
          </span>
        </div>
      </div>

      <div className="space-y-6">
        <div className="verkku-surface space-y-3 p-5">
          <h2 id={livePriceId} className="font-display text-lg font-bold text-verkku-light">
            {t.product.livePriceTitle}
          </h2>

          <PriceDisplay
            locale={locale}
            amountUsd={effectivePrice}
            pricesLocked={priceLocked}
            initialRates={initialRates}
            size="lg"
          />

          {!priceLocked && effectivePrice !== null && quote ? (
            <p className="text-xs text-verkku-mint-soft">
              {t.product.priceRefreshedAt}
              {quotedAt ? `: ${quotedAt}` : ''}
            </p>
          ) : null}

          <p className="text-xs leading-relaxed text-verkku-stone">{t.product.livePriceHint}</p>

          <div className="flex flex-wrap items-center gap-3">
            <button
              type="button"
              onClick={checkPrice}
              disabled={checking || coolingDown}
              aria-describedby={livePriceId}
              className="rounded-lg bg-verkku-green px-4 py-2 text-sm font-semibold text-verkku-light transition-colors hover:bg-verkku-emerald disabled:cursor-not-allowed disabled:opacity-50 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-verkku-neon"
            >
              {checking
                ? t.product.checkingPrice
                : quote
                  ? t.product.refreshPrice
                  : t.product.checkPrice}
            </button>

            {coolingDown ? (
              <span role="status" className="text-xs font-semibold text-verkku-mint-soft">
                {`${t.errors.retry} · ${cooldownRemaining}s`}
              </span>
            ) : null}
          </div>

          {pricesLockedError ? (
            <div
              role="alert"
              className="space-y-1 rounded-lg border border-verkku-mint-soft/30 bg-verkku-bg-deep/60 p-3"
            >
              <p className="text-sm font-semibold text-verkku-light">{t.product.pricesLockedTitle}</p>
              <p className="text-xs leading-relaxed text-verkku-stone">{t.product.pricesLockedBody}</p>
            </div>
          ) : null}

          {priceError && !pricesLockedError ? (
            <div
              role="alert"
              className="space-y-1 rounded-lg border border-verkku-mint-soft/30 bg-verkku-bg-deep/60 p-3"
            >
              <p className="text-sm font-semibold text-verkku-light">{t.errors.priceUnavailableTitle}</p>
              <p className="text-xs leading-relaxed text-verkku-stone">{t.errors.priceUnavailableBody}</p>
              {ERROR_CODE_PATTERN.test(priceError.code ?? '') ? (
                <p className="text-[11px] text-verkku-mint-soft">
                  {t.errors.errorCodeLabel}: <span className="font-mono">{priceError.code}</span>
                </p>
              ) : null}
            </div>
          ) : null}
        </div>

        <div className="rounded-xl border border-verkku-neon/35 bg-verkku-neon/5 p-5" role="note">
          <p className="inline-flex w-fit items-center gap-1 rounded-full bg-verkku-neon px-2.5 py-0.5 text-[11px] font-bold uppercase tracking-wide text-verkku-bg-deep">
            {t.product.comingSoonBadge}
          </p>
          <h2 className="mt-2 font-display text-lg font-bold text-verkku-light">{t.product.comingSoonTitle}</h2>
          <p className="mt-1 text-sm leading-relaxed text-verkku-stone">{t.product.comingSoonBody}</p>
        </div>

        <section className="verkku-surface space-y-2 p-5" aria-labelledby={`${requirementsId}-title`}>
          <h2 id={`${requirementsId}-title`} className="font-display text-base font-bold text-verkku-light">
            {t.product.requirementsTitle}
          </h2>

          {requirements.length > 0 ? (
            <>
              <p className="text-xs leading-relaxed text-verkku-stone">{t.product.requirementsHint}</p>
              <ul className="space-y-2">
                {requirements.map((requirement) => (
                  <li
                    key={requirement.fieldName}
                    className="rounded-lg border border-verkku-mint-soft/25 bg-verkku-bg-deep/50 p-3"
                  >
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <span className="font-mono text-xs font-semibold text-verkku-light">
                        {requirement.fieldName}
                      </span>
                      <span className="rounded-full border border-verkku-mint-soft/35 px-2 py-0.5 font-mono text-[11px] text-verkku-mint-soft">
                        {requirement.dataType}
                      </span>
                    </div>

                    {requirement.placeholder ? (
                      <p className="mt-1 text-xs text-verkku-stone">{requirement.placeholder}</p>
                    ) : null}

                    {requirement.minLength !== null || requirement.maxLength !== null ? (
                      <p className="mt-1 font-mono text-[11px] text-verkku-mint-soft">
                        {requirement.minLength ?? 0}
                        {'–'}
                        {requirement.maxLength ?? '∞'}
                      </p>
                    ) : null}

                    {requirement.selectOptions.length > 0 ? (
                      <ul className="mt-2 flex flex-wrap gap-1.5">
                        {requirement.selectOptions.map((option) => (
                          <li
                            key={option}
                            className="rounded-md border border-verkku-mint-soft/30 bg-verkku-surface px-2 py-0.5 text-[11px] text-verkku-stone"
                          >
                            {option}
                          </li>
                        ))}
                      </ul>
                    ) : null}
                  </li>
                ))}
              </ul>
            </>
          ) : (
            <p className="text-xs leading-relaxed text-verkku-stone">{t.product.requirementsEmpty}</p>
          )}
        </section>

        {siblingItems.length > 0 ? (
          <section className="verkku-surface space-y-2 p-5">
            <h2 className="font-display text-base font-bold text-verkku-light">{t.product.siblingTitle}</h2>
            <p className="text-xs leading-relaxed text-verkku-stone">{t.product.siblingHint}</p>
            <ul className="flex flex-wrap gap-2">
              {siblingItems.map((sibling) => (
                <li key={`${sibling.providerUid}-${sibling.externalId}`}>
                  <a
                    href={`/${locale}/product/${encodeURIComponent(sibling.providerUid)}/${encodeURIComponent(sibling.externalId)}`}
                    className="inline-flex rounded-lg border border-verkku-mint-soft/30 bg-verkku-bg-deep/60 px-3 py-1.5 text-xs font-semibold text-verkku-stone transition-colors hover:border-verkku-neon/60 hover:text-verkku-light focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-verkku-neon"
                  >
                    {sibling.name}
                  </a>
                </li>
              ))}
            </ul>
          </section>
        ) : null}

        {relatedRegionCategories.length > 0 ? (
          <section className="verkku-surface space-y-2 p-5">
            <h2 className="font-display text-base font-bold text-verkku-light">{t.product.crossRegionTitle}</h2>
            <p className="text-xs leading-relaxed text-verkku-stone">{t.product.crossRegionHint}</p>
            <ul className="flex flex-wrap gap-2">
              {relatedRegionCategories.map((category) => {
                const meta = getRegionMeta(category.region, locale);

                return (
                  <li key={`${category.providerUid}-${category.externalId}`}>
                    <a
                      href={`/${locale}/category/${encodeURIComponent(category.providerUid)}/${encodeURIComponent(category.externalId)}`}
                      className="inline-flex items-center gap-2 rounded-lg border border-verkku-mint-soft/30 bg-verkku-bg-deep/60 px-3 py-1.5 text-xs font-semibold text-verkku-stone transition-colors hover:border-verkku-neon/60 hover:text-verkku-light focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-verkku-neon"
                    >
                      <span className="rounded-full bg-verkku-surface px-2 py-0.5 text-[10px] font-bold tracking-wide text-verkku-mint">
                        {meta.shortLabel}
                      </span>
                      {category.name}
                    </a>
                  </li>
                );
              })}
            </ul>
          </section>
        ) : null}

        <a
          href={`/${locale}/catalog`}
          className="inline-flex items-center gap-1 text-sm font-semibold text-verkku-mint underline decoration-verkku-mint/50 underline-offset-4 transition-colors hover:text-verkku-light focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-verkku-neon"
        >
          <span aria-hidden="true">←</span>
          {t.product.backToCatalog}
        </a>
      </div>
    </div>
  );
}
