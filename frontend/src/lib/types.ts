/**
 * Public API v1 DTO contracts (see backend `VerkkuGames.Api`).
 *
 * Provider identity is always the opaque `providerUid` GUID: the wholesale
 * provider name must never reach the browser, URLs or JSON-LD.
 */

export type CurrencyCode = 'USD' | 'USDT' | 'USDC' | 'BTC' | 'ETH';

export type RegionCode = 'USA' | 'GLOBAL' | 'MX' | 'PA' | 'CO' | 'OTHER';

export type ProductType = 'topup' | 'voucher';

export type RequirementDataType = 'text' | 'number' | 'single_select' | 'multi_select';

export interface MoneyDto {
  amount: number;
  currency: CurrencyCode;
}

export interface BigCategoryDto {
  providerUid: string;
  externalId: string;
  name: string;
  description: string;
  imageUrl: string | null;
}

export interface CategoryDto {
  providerUid: string;
  externalId: string;
  bigCategoryId: string;
  name: string;
  description: string;
  imageUrl: string | null;
  region: RegionCode;
}

export interface CatalogItemDto {
  providerUid: string;
  externalId: string;
  categoryId: string;
  categoryName: string;
  name: string;
  description: string;
  imageUrl: string | null;
  productType: ProductType;
  region: RegionCode;
  retailPriceUsd: MoneyDto | null;
  inStock: boolean;
  pricesLocked: boolean;
}

export interface CategoryRequirementDto {
  fieldName: string;
  dataType: RequirementDataType;
  placeholder: string | null;
  selectOptions: string[];
  minLength: number | null;
  maxLength: number | null;
}

export interface CatalogItemDetailDto {
  item: CatalogItemDto;
  requirements: CategoryRequirementDto[];
  siblingItems: CatalogItemDto[];
  relatedRegionCategories: CategoryDto[];
}

export interface ItemPriceQuoteDto {
  providerUid: string;
  externalItemId: string;
  itemName: string;
  retailPriceUsd: MoneyDto;
  inStock: boolean;
  quotedAtUtc: string;
}

export interface ExchangeRatesDto {
  baseCurrency: CurrencyCode;
  ratesPerUsd: Record<CurrencyCode, number>;
  retrievedAtUtc: string;
}

export interface PagedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface ProblemDetailsDto {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  retryAfterSeconds?: number;
  traceId?: string;
}
