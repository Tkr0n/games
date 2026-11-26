export const LOCALES = ['es', 'en'] as const;

export type Locale = (typeof LOCALES)[number];

export const DEFAULT_LOCALE: Locale = 'es';

/** Cookie read by `detectPreferredLocale`; kept short and same-site friendly. */
export const LOCALE_COOKIE_NAME = 'locale';

/** HTML `lang` attribute per locale. */
export const HTML_LANG: Record<Locale, string> = {
  es: 'es',
  en: 'en',
};

/** Open Graph locale codes used by the SEO head component. */
export const OG_LOCALE: Record<Locale, string> = {
  es: 'es_VE',
  en: 'en_US',
};

/**
 * Single source of truth for every user-facing string.
 *
 * `es.ts` and `en.ts` are both typed as `Dictionary`, so TypeScript fails the
 * build the moment one locale drifts from the other. Keep this interface
 * nested by page surface: the leaf paths are what the parity test compares.
 */
export interface Dictionary {
  meta: {
    siteName: string;
    tagline: string;
    defaultDescription: string;
  };
  common: {
    loading: string;
    refreshing: string;
    retry: string;
    close: string;
    cancel: string;
    clear: string;
    readOnly: string;
    viewAll: string;
  };
  nav: {
    home: string;
    catalog: string;
    studio: string;
    skipToContent: string;
    languageLabel: string;
    currencyLabel: string;
    menuLabel: string;
  };
  hero: {
    eyebrow: string;
    title: string;
    highlight: string;
    subtitle: string;
    ctaCatalog: string;
    ctaStudio: string;
    trustInstant: string;
    trustSecure: string;
    trustRegional: string;
  };
  home: {
    typeFilterTitle: string;
    typeFilterSubtitle: string;
    typeGames: string;
    typeGamesDesc: string;
    typeGiftCards: string;
    typeGiftCardsDesc: string;
  };
  regions: {
    title: string;
    subtitle: string;
    whyTitle: string;
    whyBody: string;
    currentLabel: string;
    selectLabel: string;
    globalNote: string;
    otherNote: string;
    venezuelaNote: string;
  };
  currency: {
    title: string;
    subtitle: string;
    label: string;
    usdHint: string;
    unavailableRate: string;
    updatedAt: string;
    referenceLabel: string;
  };
  catalog: {
    title: string;
    subtitle: string;
    filtersTitle: string;
    filters: {
      searchLabel: string;
      searchPlaceholder: string;
      regionLabel: string;
      regionHint: string;
      includeGlobal: string;
      includeGlobalHint: string;
      productTypeLabel: string;
      productTypeAll: string;
      productTypeTopup: string;
      productTypeVoucher: string;
      inStockOnly: string;
      apply: string;
      reset: string;
      resultsLabel: string;
      activeFilters: string;
    };
    pagination: {
      previous: string;
      next: string;
      pageOf: string;
    };
    card: {
      fromPrice: string;
      outOfStock: string;
      pricesLocked: string;
      viewDetails: string;
      livePrice: string;
      regionLabel: string;
    };
    emptyTitle: string;
    emptyBody: string;
    bigCategoriesTitle: string;
    bigCategoriesSubtitle: string;
  };
  product: {
    livePriceTitle: string;
    livePriceHint: string;
    checkPrice: string;
    checkingPrice: string;
    refreshPrice: string;
    priceRefreshedAt: string;
    priceUnavailable: string;
    pricesLockedTitle: string;
    pricesLockedBody: string;
    requirementsTitle: string;
    requirementsHint: string;
    requirementsEmpty: string;
    requirementRequired: string;
    siblingTitle: string;
    siblingHint: string;
    crossRegionTitle: string;
    crossRegionHint: string;
    comingSoonBadge: string;
    comingSoonTitle: string;
    comingSoonBody: string;
    inStock: string;
    outOfStock: string;
    retailPriceLabel: string;
    referenceLabel: string;
    regionLabel: string;
    productTypeTopup: string;
    productTypeVoucher: string;
    backToCatalog: string;
  };
  errors: {
    notFoundTitle: string;
    notFoundBody: string;
    notFoundCta: string;
    apiUnavailableTitle: string;
    apiUnavailableBody: string;
    priceUnavailableTitle: string;
    priceUnavailableBody: string;
    rateUnavailableTitle: string;
    rateUnavailableBody: string;
    genericTitle: string;
    genericBody: string;
    retry: string;
    errorCodeLabel: string;
  };
  studio: {
    eyebrow: string;
    title: string;
    body: string;
    badge: string;
    cta: string;
    footnote: string;
  };
  footer: {
    tagline: string;
    catalogTitle: string;
    companyTitle: string;
    legalTitle: string;
    regionsTitle: string;
    about: string;
    contact: string;
    studio: string;
    allRegions: string;
    terms: string;
    privacy: string;
    cookies: string;
    rights: string;
    disclaimer: string;
    languageLabel: string;
    currencyLabel: string;
  };
}
