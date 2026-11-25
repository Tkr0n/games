namespace VerkkuGames.Domain.Catalog;

public sealed record CatalogItemDetail(
    CatalogItem Item,
    IReadOnlyList<CategoryRequirement> Requirements,
    IReadOnlyList<CatalogItem> SiblingItems,
    IReadOnlyList<CatalogCategory> RelatedRegionCategories);
