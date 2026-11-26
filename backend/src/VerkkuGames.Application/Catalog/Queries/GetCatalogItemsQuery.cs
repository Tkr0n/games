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
    int PageSize = 24,
    string? Platform = null,
    string? Genre = null,
    int? ReleaseYearFrom = null,
    int? ReleaseYearTo = null,
    bool MultiplayerOnly = false);

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

        RuleFor(query => query.Platform)
            .MaximumLength(64)
            .Matches("^[A-Za-z0-9 _-]+$")
            .When(query => !string.IsNullOrWhiteSpace(query.Platform));

        RuleFor(query => query.Genre)
            .MaximumLength(64)
            .Matches("^[A-Za-z0-9 _-]+$")
            .When(query => !string.IsNullOrWhiteSpace(query.Genre));

        RuleFor(query => query.ReleaseYearFrom)
            .InclusiveBetween(1970, 2100)
            .When(query => query.ReleaseYearFrom.HasValue);

        RuleFor(query => query.ReleaseYearTo)
            .InclusiveBetween(1970, 2100)
            .GreaterThanOrEqualTo(query => query.ReleaseYearFrom ?? 0)
            .When(query => query.ReleaseYearTo.HasValue);
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
            .Where(item => MatchesPlatform(item, query.Platform))
            .Where(item => MatchesGenre(item, query.Genre))
            .Where(item => MatchesYear(item, query.ReleaseYearFrom, query.ReleaseYearTo))
            .Where(item => !query.MultiplayerOnly || item.Metadata?.IsMultiplayer == true)
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

    private static bool MatchesPlatform(CatalogItem item, string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return true;
        var requested = platform.Trim();

        return string.Equals(item.Platform, requested, StringComparison.OrdinalIgnoreCase)
            || item.Metadata?.SupportedPlatforms.Any(candidate =>
                string.Equals(candidate, requested, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static bool MatchesGenre(CatalogItem item, string? genre) =>
        string.IsNullOrWhiteSpace(genre)
        || item.Metadata?.Genres.Any(candidate =>
            string.Equals(candidate, genre.Trim(), StringComparison.OrdinalIgnoreCase)) == true;

    private static bool MatchesYear(CatalogItem item, int? from, int? to)
    {
        if (from is null && to is null) return true;
        var year = item.Metadata?.ReleaseYear;
        if (year is null) return false;
        return (from is null || year >= from) && (to is null || year <= to);
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
