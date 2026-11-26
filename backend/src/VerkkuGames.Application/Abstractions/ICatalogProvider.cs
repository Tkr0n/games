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

    /// <summary>
    /// Direct lookup by external item id. Providers whose catalog cannot be
    /// listed in one call (e.g. game keys keyed by game) override this; the
    /// default scans the full listing so existing providers need no change.
    /// </summary>
    Task<Result<CatalogItem>> GetItemByIdAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return ScanForItemAsync(this, itemId, cancellationToken);
    }

    private static async Task<Result<CatalogItem>> ScanForItemAsync(
        ICatalogProvider provider,
        string itemId,
        CancellationToken cancellationToken)
    {
        var items = await provider.GetItemsAsync(null, cancellationToken).ConfigureAwait(false);
        if (items.IsFailure)
        {
            return Result<CatalogItem>.Failure(items.Error);
        }

        var match = items.Value.FirstOrDefault(candidate =>
            string.Equals(candidate.ExternalId, itemId, StringComparison.Ordinal));

        return match is null
            ? Result<CatalogItem>.Failure(Error.NotFound("item_not_found", "The requested catalog item was not found."))
            : Result<CatalogItem>.Success(match);
    }

    Task<Result<ItemPriceQuote>> GetItemPriceAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<CategoryRequirement>>> GetCategoryRequirementsAsync(
        string categoryId,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> CheckHealthAsync(CancellationToken cancellationToken = default);
}
