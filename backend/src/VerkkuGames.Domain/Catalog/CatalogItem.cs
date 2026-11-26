using VerkkuGames.Domain.ValueObjects;

namespace VerkkuGames.Domain.Catalog;

public sealed record CatalogItem(
    Guid ProviderUid,
    string ExternalId,
    string CategoryId,
    string CategoryName,
    string Name,
    string Description,
    string? ImageUrl,
    ProductType ProductType,
    RegionCode Region,
    Money? RetailPriceUsd,
    bool InStock,
    bool PricesLocked,
    GameKeyVariant? KeyVariant = null,
    RegionLockInfo? RegionLock = null,
    string? Platform = null);
