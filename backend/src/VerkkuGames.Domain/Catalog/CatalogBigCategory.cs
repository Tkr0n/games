using VerkkuGames.Domain.ValueObjects;

namespace VerkkuGames.Domain.Catalog;

public sealed record CatalogBigCategory(
    Guid ProviderUid,
    string ExternalId,
    string Name,
    string Description,
    string? ImageUrl);
