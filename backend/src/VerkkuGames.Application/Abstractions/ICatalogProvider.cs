using VerkkuGames.Domain.Catalog;
using VerkkuGames.Domain.Common;

namespace VerkkuGames.Application.Abstractions;

public interface ICatalogProvider
{
    string InternalKey { get; }

    Guid ProviderUid { get; }

    Task<Result<IReadOnlyList<CatalogBigCategory>>> GetBigCategoriesAsync(
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<CatalogCategory>>> GetCategoriesAsync(
        string? bigCategoryId = null,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<CatalogItem>>> GetItemsAsync(
        string? categoryId = null,
        CancellationToken cancellationToken = default);

    Task<Result<ItemPriceQuote>> GetItemPriceAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<CategoryRequirement>>> GetCategoryRequirementsAsync(
        string categoryId,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> CheckHealthAsync(CancellationToken cancellationToken = default);
}
