import { useCallback, useEffect, useId, useRef, useState } from 'react';

import type { Locale } from '../../i18n/config';
import { getDictionary } from '../../i18n/utils';
import { getRegionMeta } from '../../lib/regions';
import { sanitizeExternalImageUrl } from '../../lib/seo';
import type {
  CatalogItemDto,
  ExchangeRatesDto,
  PagedResponse,
  ProblemDetailsDto,
  ProductType,
  RegionCode,
} from '../../lib/types';
import PriceDisplay from './PriceDisplay';
import RegionSelector, { type RegionSelection } from './RegionSelector';

export interface CatalogFilters {
  /** `null` means "all regions"; nothing is sent to the backend. */
  region: RegionCode | null;
  includeGlobal: boolean;
  productType: ProductType | null;
  inStockOnly: boolean;
  search: string;
  /** Optional category scope, preserved from the URL. */
  categoryId: string | null;
  page: number;
}

interface CatalogExplorerProps {
  locale: Locale;
  initialPage: PagedResponse<CatalogItemDto> | null;
  initialFilters: CatalogFilters;
  initialError: ProblemDetailsDto | null;
  /** Public rate snapshot rendered by SSR; never contains secrets. */
  initialRates: ExchangeRatesDto | null;
  pageSize?: number;
}

const DEFAULT_PAGE_SIZE = 24;
const SEARCH_DEBOUNCE_MS = 400;
const ERROR_CODE_PATTERN = /^[a-z0-9_.-]{1,64}$/i;

const REGION_BADGE_CLASSES: Record<string, string> = {
  emerald: 'border-verkku-neon/40 bg-verkku-neon/10 text-verkku-mint',
  mint: 'border-verkku-mint/40 bg-verkku-mint/10 text-verkku-mint',
  teal: 'border-verkku-teal-bright/50 bg-verkku-teal/30 text-verkku-mint-soft',
  'mint-soft': 'border-verkku-mint-soft/40 bg-verkku-mint-soft/10 text-verkku-mint-soft',
  'teal-bright': 'border-verkku-teal-bright/50 bg-verkku-teal-bright/10 text-verkku-mint-soft',
  stone: 'border-verkku-stone/30 bg-verkku-stone/10 text-verkku-stone',
};

const PRODUCT_TYPE_CLASSES: Record<ProductType, string> = {
  topup: 'border-verkku-neon/40 bg-verkku-neon/10 text-verkku-mint',
  voucher: 'border-verkku-mint-soft/40 bg-verkku-mint-soft/10 text-verkku-mint-soft',
};

function isPagedItems(value: unknown): value is PagedResponse<CatalogItemDto> {
  if (!value || typeof value !== 'object') return false;

  const candidate = value as Partial<PagedResponse<CatalogItemDto>>;

  return (
    Array.isArray(candidate.items) &&
    typeof candidate.page === 'number' &&
    typeof candidate.pageSize === 'number' &&
    typeof candidate.totalCount === 'number' &&
    typeof candidate.totalPages === 'number'
  );
}

function toProblem(value: unknown, status: number): ProblemDetailsDto {
  const fallback: ProblemDetailsDto = {
    status,
    code: 'catalog_unavailable',
    title: 'Catalog unavailable',
  };

  if (!value || typeof value !== 'object') return fallback;

  const candidate = value as Record<string, unknown>;
  const code = typeof candidate.code === 'string' && ERROR_CODE_PATTERN.test(candidate.code)
    ? candidate.code
    : fallback.code;
  const title = typeof candidate.title === 'string' ? candidate.title : fallback.title;

  return { status, code, title };
}

function buildItemsQuery(filters: CatalogFilters, pageSize: number): string {
  const params = new URLSearchParams();

  if (filters.region) params.set('region', filters.region);
  if (filters.region && filters.region !== 'GLOBAL' && filters.region !== 'OTHER') {
    if (filters.includeGlobal) params.set('includeGlobal', 'true');
  }
  if (filters.productType) params.set('productType', filters.productType);
  if (filters.inStockOnly) params.set('inStockOnly', 'true');
  if (filters.search) params.set('search', filters.search);
  if (filters.categoryId) params.set('categoryId', filters.categoryId);

  params.set('page', String(filters.page));
  params.set('pageSize', String(pageSize));

  return params.toString();
}

function buildUrlQuery(filters: CatalogFilters): string {
  const params = new URLSearchParams();

  if (filters.region) params.set('region', filters.region);
  if (filters.region && filters.region !== 'GLOBAL' && filters.region !== 'OTHER' && filters.includeGlobal) {
    params.set('includeGlobal', 'true');
  }
  if (filters.productType) params.set('productType', filters.productType);
  if (filters.inStockOnly) params.set('inStockOnly', 'true');
  if (filters.search) params.set('q', filters.search);
  if (filters.categoryId) params.set('categoryId', filters.categoryId);
  if (filters.page > 1) params.set('page', String(filters.page));

  return params.toString();
}

function formatCount(value: number, locale: Locale): string {
  try {
    return new Intl.NumberFormat(locale).format(value);
  } catch {
    return String(value);
  }
}

/**
 * Client-side catalog: filters, search, region and pagination all talk to the
 * same-origin BFF (`/api/items`) and rewrite the URL with `replaceState`.
 *
 * When the backend is unreachable the island shows a localized notice with a
 * retry action. It never renders placeholder products.
 */
export default function CatalogExplorer({
  locale,
  initialPage,
  initialFilters,
  initialError,
  initialRates,
  pageSize = DEFAULT_PAGE_SIZE,
}: CatalogExplorerProps) {
  const t = getDictionary(locale);

  const [filters, setFilters] = useState<CatalogFilters>(initialFilters);
  const [data, setData] = useState<PagedResponse<CatalogItemDto> | null>(initialPage);
  const [error, setError] = useState<ProblemDetailsDto | null>(initialError);
  const [loading, setLoading] = useState(false);
  const [searchInput, setSearchInput] = useState(initialFilters.search);

  const filtersRef = useRef(filters);
  filtersRef.current = filters;

  const abortRef = useRef<AbortController | null>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const resultsId = useId();
  const searchId = useId();
  const stockId = useId();

  const load = useCallback(
    (next: CatalogFilters) => {
      abortRef.current?.abort();

      const controller = new AbortController();
      abortRef.current = controller;

      filtersRef.current = next;
      setFilters(next);
      setLoading(true);
      setError(null);

      const query = buildUrlQuery(next);
      try {
        window.history.replaceState(
          {},
          '',
          `${window.location.pathname}${query ? `?${query}` : ''}`,
        );
      } catch {
        // History API can be restricted in embedded webviews; filters still work.
      }

      void (async () => {
        try {
          const response = await fetch(`/api/items?${buildItemsQuery(next, pageSize)}`, {
            method: 'GET',
            headers: { Accept: 'application/json' },
            signal: controller.signal,
          });

          const payload: unknown = await response.json().catch(() => null);

          if (controller.signal.aborted) return;

          if (!response.ok) {
            setData(null);
            setError(toProblem(payload, response.status));
            return;
          }

          if (isPagedItems(payload)) {
            setData(payload);
            setError(null);
          } else {
            setData(null);
            setError(toProblem(null, 503));
          }
        } catch (caught) {
          if (controller.signal.aborted) return;
          if (caught instanceof Error && caught.name === 'AbortError') return;

          setData(null);
          setError(toProblem(null, 503));
        } finally {
          if (!controller.signal.aborted) setLoading(false);
        }
      })();
    },
    [pageSize],
  );

  useEffect(() => {
    return () => {
      abortRef.current?.abort();
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, []);

  const handleRegion = (next: RegionSelection): void => {
    load({ ...filtersRef.current, region: next.region, includeGlobal: next.includeGlobal, page: 1 });
  };

  const handleProductType = (next: ProductType | null): void => {
    load({ ...filtersRef.current, productType: next, page: 1 });
  };

  const handleStock = (next: boolean): void => {
    load({ ...filtersRef.current, inStockOnly: next, page: 1 });
  };

  const handleSearchInput = (value: string): void => {
    setSearchInput(value);

    if (debounceRef.current) clearTimeout(debounceRef.current);

    debounceRef.current = setTimeout(() => {
      const trimmed = value.trim();
      if (trimmed === filtersRef.current.search) return;
      load({ ...filtersRef.current, search: trimmed, page: 1 });
    }, SEARCH_DEBOUNCE_MS);
  };

  const handleSearchSubmit = (): void => {
    if (debounceRef.current) clearTimeout(debounceRef.current);

    const trimmed = searchInput.trim();
    load({ ...filtersRef.current, search: trimmed, page: 1 });
  };

  const handleReset = (): void => {
    if (debounceRef.current) clearTimeout(debounceRef.current);

    setSearchInput('');
    load({
      region: null,
      includeGlobal: false,
      productType: null,
      inStockOnly: false,
      search: '',
      categoryId: filtersRef.current.categoryId,
      page: 1,
    });
  };

  const goToPage = (page: number): void => {
    if (page < 1) return;
    if (data && page > data.totalPages) return;

    load({ ...filtersRef.current, page });
  };

  const items = data?.items ?? [];
  const totalPages = data?.totalPages ?? 0;
  const showEmpty = !loading && !error && items.length === 0;

  return (
    <section className="space-y-6" aria-busy={loading}>
      <div className="verkku-surface p-4 sm:p-6">
        <form
          role="search"
          onSubmit={(event) => {
            event.preventDefault();
            handleSearchSubmit();
          }}
          className="space-y-4"
        >
          <div className="space-y-1.5">
            <label htmlFor={searchId} className="text-xs font-semibold text-verkku-stone">
              {t.catalog.filters.searchLabel}
            </label>
            <input
              id={searchId}
              type="search"
              value={searchInput}
              onChange={(event) => handleSearchInput(event.target.value)}
              placeholder={t.catalog.filters.searchPlaceholder}
              maxLength={100}
              autoComplete="off"
              className="w-full rounded-lg border border-verkku-mint-soft/30 bg-verkku-bg-deep px-3 py-2 text-sm text-verkku-light placeholder:text-verkku-mint-soft/60 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-verkku-neon"
            />
          </div>

          <RegionSelector
            locale={locale}
            value={{ region: filters.region, includeGlobal: filters.includeGlobal }}
            onChange={handleRegion}
          />

          <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <div className="space-y-1.5">
              <span className="text-xs font-semibold text-verkku-stone">
                {t.catalog.filters.productTypeLabel}
              </span>
              <div
                className="flex flex-wrap gap-2"
                role="group"
                aria-label={t.catalog.filters.productTypeLabel}
              >
                {[
                  { value: null as ProductType | null, label: t.catalog.filters.productTypeAll },
                  { value: 'topup' as ProductType, label: t.catalog.filters.productTypeTopup },
                  { value: 'voucher' as ProductType, label: t.catalog.filters.productTypeVoucher },
                ].map((option) => {
                  const active = filters.productType === option.value;

                  return (
                    <button
                      key={option.label}
                      type="button"
                      aria-pressed={active}
                      onClick={() => handleProductType(option.value)}
                      className={`rounded-full border px-3 py-1.5 text-xs font-semibold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-verkku-neon ${
                        active
                          ? 'border-verkku-neon bg-verkku-neon text-verkku-bg-deep'
                          : 'border-verkku-mint-soft/30 bg-verkku-surface text-verkku-stone hover:border-verkku-neon/60 hover:text-verkku-light'
                      }`}
                    >
                      {option.label}
                    </button>
                  );
                })}
              </div>
            </div>

            <div className="flex flex-wrap items-center gap-3">
              <div className="flex items-center gap-2">
                <input
                  id={stockId}
                  type="checkbox"
                  checked={filters.inStockOnly}
                  onChange={(event) => handleStock(event.target.checked)}
                  className="size-4 rounded border-verkku-mint-soft/50 bg-verkku-surface accent-verkku-neon focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-verkku-neon"
                />
                <label htmlFor={stockId} className="text-xs font-semibold text-verkku-stone">
                  {t.catalog.filters.inStockOnly}
                </label>
              </div>

              <button
                type="button"
                onClick={handleReset}
                className="rounded-lg border border-verkku-mint-soft/30 bg-verkku-surface px-3 py-1.5 text-xs font-semibold text-verkku-stone transition-colors hover:border-verkku-neon/60 hover:text-verkku-light focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-verkku-neon"
              >
                {t.catalog.filters.reset}
              </button>
            </div>
          </div>
        </form>
      </div>

      <div className="flex flex-wrap items-baseline justify-between gap-2" aria-live="polite" id={resultsId}>
        <p className="text-sm text-verkku-mint-soft">
          {t.catalog.filters.resultsLabel}:{' '}
          <span className="font-semibold text-verkku-light tabular-nums">
            {formatCount(data?.totalCount ?? 0, locale)}
          </span>
        </p>
        {loading ? <span className="text-xs text-verkku-mint-soft">{t.common.refreshing}</span> : null}
      </div>

      {error ? (
        <div
          role="alert"
          className="verkku-surface flex flex-col items-start gap-3 border-verkku-mint-soft/25 p-6"
        >
          <h2 className="font-display text-lg font-bold text-verkku-light">
            {t.errors.apiUnavailableTitle}
          </h2>
          <p className="max-w-prose text-sm leading-relaxed text-verkku-stone">
            {t.errors.apiUnavailableBody}
          </p>
          {error.code && ERROR_CODE_PATTERN.test(error.code) ? (
            <p className="text-xs text-verkku-mint-soft">
              {t.errors.errorCodeLabel}: <span className="font-mono">{error.code}</span>
            </p>
          ) : null}
          <button
            type="button"
            onClick={() => load(filtersRef.current)}
            className="rounded-lg bg-verkku-green px-4 py-2 text-sm font-semibold text-verkku-light transition-colors hover:bg-verkku-emerald focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-verkku-neon"
          >
            {t.common.retry}
          </button>
        </div>
      ) : null}

      {showEmpty ? (
        <div className="verkku-surface flex flex-col items-start gap-3 p-6">
          <h2 className="font-display text-lg font-bold text-verkku-light">{t.catalog.emptyTitle}</h2>
          <p className="max-w-prose text-sm leading-relaxed text-verkku-stone">{t.catalog.emptyBody}</p>
          <button
            type="button"
            onClick={handleReset}
            className="rounded-lg border border-verkku-mint-soft/30 bg-verkku-surface px-4 py-2 text-sm font-semibold text-verkku-stone transition-colors hover:border-verkku-neon/60 hover:text-verkku-light focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-verkku-neon"
          >
            {t.catalog.filters.reset}
          </button>
        </div>
      ) : null}

      {items.length > 0 ? (
        <ul className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {items.map((item) => {
            const regionMeta = getRegionMeta(item.region, locale);
            const regionClasses = REGION_BADGE_CLASSES[regionMeta.badgeTone] ?? REGION_BADGE_CLASSES.stone;
            const typeLabel =
              item.productType === 'topup'
                ? t.catalog.filters.productTypeTopup
                : t.catalog.filters.productTypeVoucher;
            const productHref = `/${locale}/product/${encodeURIComponent(item.providerUid)}/${encodeURIComponent(item.externalId)}`;

            return (
              <li key={`${item.providerUid}-${item.externalId}`} className="h-full">
                <a
                  href={productHref}
                  className="group flex h-full flex-col overflow-hidden rounded-xl border border-verkku-mint-soft/20 bg-verkku-surface/90 transition-colors hover:border-verkku-neon/50 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-verkku-neon"
                >
                  <div className="relative aspect-[4/3] overflow-hidden bg-verkku-bg-deep">
                    <img
                      src={sanitizeExternalImageUrl(item.imageUrl)}
                      alt={item.name}
                      loading="lazy"
                      decoding="async"
                      referrerPolicy="no-referrer"
                      width="480"
                      height="360"
                      className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-[1.03] motion-reduce:transform-none"
                    />
                  </div>

                  <div className="flex flex-1 flex-col gap-2 p-4">
                    <p className="text-[11px] font-medium uppercase tracking-wide text-verkku-mint-soft">
                      {item.categoryName}
                    </p>
                    <h3 className="text-sm font-semibold leading-snug text-verkku-light">{item.name}</h3>

                    <div className="flex flex-wrap gap-1.5">
                      <span
                        className={`rounded-full border px-2 py-0.5 text-[11px] font-semibold ${regionClasses}`}
                      >
                        {t.catalog.card.regionLabel}: {regionMeta.shortLabel}
                      </span>
                      <span
                        className={`rounded-full border px-2 py-0.5 text-[11px] font-semibold ${PRODUCT_TYPE_CLASSES[item.productType]}`}
                      >
                        {typeLabel}
                      </span>
                      <span className="rounded-full border border-verkku-mint-soft/35 bg-verkku-bg-deep/60 px-2 py-0.5 text-[11px] font-semibold text-verkku-stone">
                        {item.inStock ? t.product.inStock : t.product.outOfStock}
                      </span>
                    </div>

                    <div className="mt-auto flex items-end justify-between gap-2 pt-2">
                      <PriceDisplay
                        locale={locale}
                        amountUsd={item.retailPriceUsd?.amount ?? null}
                        pricesLocked={item.pricesLocked}
                        initialRates={initialRates}
                        showFrom
                        size="sm"
                      />
                      <span className="text-xs font-semibold text-verkku-mint transition-colors group-hover:text-verkku-light">
                        {t.catalog.card.viewDetails}
                      </span>
                    </div>
                  </div>
                </a>
              </li>
            );
          })}
        </ul>
      ) : null}

      {totalPages > 1 ? (
        <nav
          aria-label={t.catalog.pagination.pageOf
            .replace('{page}', String(filters.page))
            .replace('{total}', String(totalPages))}
          className="flex flex-wrap items-center justify-between gap-3"
        >
          <button
            type="button"
            onClick={() => goToPage(filters.page - 1)}
            disabled={filters.page <= 1 || loading}
            className="rounded-lg border border-verkku-mint-soft/30 bg-verkku-surface px-4 py-2 text-sm font-semibold text-verkku-stone transition-colors hover:border-verkku-neon/60 hover:text-verkku-light disabled:cursor-not-allowed disabled:opacity-40 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-verkku-neon"
          >
            {t.catalog.pagination.previous}
          </button>

          <p className="text-xs font-semibold text-verkku-mint-soft">
            {t.catalog.pagination.pageOf
              .replace('{page}', String(filters.page))
              .replace('{total}', String(totalPages))}
          </p>

          <button
            type="button"
            onClick={() => goToPage(filters.page + 1)}
            disabled={filters.page >= totalPages || loading}
            className="rounded-lg border border-verkku-mint-soft/30 bg-verkku-surface px-4 py-2 text-sm font-semibold text-verkku-stone transition-colors hover:border-verkku-neon/60 hover:text-verkku-light disabled:cursor-not-allowed disabled:opacity-40 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-verkku-neon"
          >
            {t.catalog.pagination.next}
          </button>
        </nav>
      ) : null}
    </section>
  );
}
