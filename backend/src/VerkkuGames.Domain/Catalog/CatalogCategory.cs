using VerkkuGames.Domain.ValueObjects;

namespace VerkkuGames.Domain.Catalog;

public sealed record CatalogCategory(
    Guid ProviderUid,
    string ExternalId,
    string BigCategoryId,
    string Name,
    string Description,
    string? ImageUrl,
    RegionCode Region);
