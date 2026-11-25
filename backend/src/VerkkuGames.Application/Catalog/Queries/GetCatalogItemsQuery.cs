using FluentValidation;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Application.Common;
using VerkkuGames.Domain.Catalog;
using VerkkuGames.Domain.Common;
using VerkkuGames.Domain.ValueObjects;

namespace VerkkuGames.Application.Catalog.Queries;

public sealed record GetCatalogItemsQuery(
    Guid? ProviderUid = null,
    string? CategoryId = null,
    RegionCode? Region = null,
    bool IncludeGlobal = false,
    ProductType? ProductType = null,
    bool InStockOnly = false,
    string? Search = null,
    int Page = 1,
    int PageSize = 24);

public sealed class GetCatalogItemsQueryValidator : AbstractValidator<GetCatalogItemsQuery>
{
    private const string ExternalIdPattern = "^[A-Za-z0-9_-]+$";

    public GetCatalogItemsQueryValidator()
    {
        RuleFor(query => query.ProviderUid)
            .NotEqual(Guid.Empty)
            .When(query => query.ProviderUid.HasValue)
            .WithMessage("'Provider Uid' must not be an empty identifier.");

        RuleFor(query => query.CategoryId)
            .MaximumLength(64)
            .Matches(ExternalIdPattern)
            .When(query => !string.IsNullOrWhiteSpace(query.CategoryId));

        RuleFor(query => query.Search)
            .MaximumLength(100)
            .When(query => !string.IsNullOrWhiteSpace(query.Search));

        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100);
    }
}

public sealed class GetCatalogItemsQueryHandler(
    ICatalogProviderRegistry providerRegistry,
    IValidator<GetCatalogItemsQuery> validator)
    : IQueryHandler<GetCatalogItemsQuery, PagedResult<CatalogItem>>
{
    public async Task<Result<PagedResult<CatalogItem>>> HandleAsync(
        GetCatalogItemsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<PagedResult<CatalogItem>>.Failure(
                Error.Validation(
                    "validation_failed",
                    string.Join("; ", validation.Errors.Select(error => error.ErrorMessage))));
        }

        var providerResult = query.ProviderUid is { } providerUid
            ? providerRegistry.GetByUid(providerUid)
            : providerRegistry.GetDefault();

        if (providerResult.IsFailure)
        {
            return Result<PagedResult<CatalogItem>>.Failure(providerResult.Error);
        }

        var itemsResult = await providerResult.Value.GetItemsAsync(query.CategoryId, cancellationToken);
        if (itemsResult.IsFailure)
        {
            return Result<PagedResult<CatalogItem>>.Failure(itemsResult.Error);
        }

        var filtered = itemsResult.Value
            .Where(item => MatchesRegion(item.Region, query.Region, query.IncludeGlobal))
            .Where(item => string.IsNullOrWhiteSpace(query.CategoryId)
                || string.Equals(item.CategoryId, query.CategoryId, StringComparison.Ordinal))
            .Where(item => query.ProductType is not { } productType || item.ProductType == productType)
            .Where(item => !query.InStockOnly || item.InStock)
            .Where(item => string.IsNullOrWhiteSpace(query.Search)
                || item.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var totalCount = filtered.Length;
        var totalPages = totalCount == 0 ? 0 : (totalCount + query.PageSize - 1) / query.PageSize;
        var pageItems = filtered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArray();

        return Result<PagedResult<CatalogItem>>.Success(
            new PagedResult<CatalogItem>(pageItems, query.Page, query.PageSize, totalCount, totalPages));
    }

    private static bool MatchesRegion(RegionCode itemRegion, RegionCode? requestedRegion, bool includeGlobal)
    {
        if (requestedRegion is not { } region)
        {
            return true;
        }

        if (includeGlobal && region is RegionCode.USA or RegionCode.MX or RegionCode.PA or RegionCode.CO)
        {
            return itemRegion == region || itemRegion == RegionCode.GLOBAL;
        }

        return itemRegion == region;
    }
}
