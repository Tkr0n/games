using FluentValidation;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Domain.Catalog;
using VerkkuGames.Domain.Common;
using VerkkuGames.Domain.ValueObjects;

namespace VerkkuGames.Application.Catalog.Queries;

public sealed record GetCategoriesQuery(
    Guid? ProviderUid = null,
    string? BigCategoryId = null,
    RegionCode? Region = null,
    bool IncludeGlobal = false,
    string? Search = null);

public sealed class GetCategoriesQueryValidator : AbstractValidator<GetCategoriesQuery>
{
    private const string ExternalIdPattern = "^[A-Za-z0-9_-]+$";

    public GetCategoriesQueryValidator()
    {
        RuleFor(query => query.ProviderUid)
            .NotEqual(Guid.Empty)
            .When(query => query.ProviderUid.HasValue)
            .WithMessage("'Provider Uid' must not be an empty identifier.");

        RuleFor(query => query.BigCategoryId)
            .MaximumLength(64)
            .Matches(ExternalIdPattern)
            .When(query => !string.IsNullOrWhiteSpace(query.BigCategoryId));

        RuleFor(query => query.Search)
            .MaximumLength(100)
            .When(query => !string.IsNullOrWhiteSpace(query.Search));
    }
}

public sealed class GetCategoriesQueryHandler(
    ICatalogProviderRegistry providerRegistry,
    IValidator<GetCategoriesQuery> validator)
    : IQueryHandler<GetCategoriesQuery, IReadOnlyList<CatalogCategory>>
{
    public async Task<Result<IReadOnlyList<CatalogCategory>>> HandleAsync(
        GetCategoriesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<IReadOnlyList<CatalogCategory>>.Failure(
                Error.Validation(
                    "validation_failed",
                    string.Join("; ", validation.Errors.Select(error => error.ErrorMessage))));
        }

        var providerResult = query.ProviderUid is { } providerUid
            ? providerRegistry.GetByUid(providerUid)
            : providerRegistry.GetDefault();

        if (providerResult.IsFailure)
        {
            return Result<IReadOnlyList<CatalogCategory>>.Failure(providerResult.Error);
        }

        var categoriesResult = await providerResult.Value.GetCategoriesAsync(query.BigCategoryId, cancellationToken);
        if (categoriesResult.IsFailure)
        {
            return categoriesResult;
        }

        var categories = categoriesResult.Value
            .Where(category => MatchesRegion(category.Region, query.Region, query.IncludeGlobal))
            .Where(category => string.IsNullOrWhiteSpace(query.BigCategoryId)
                || string.Equals(category.BigCategoryId, query.BigCategoryId, StringComparison.Ordinal))
            .Where(category => string.IsNullOrWhiteSpace(query.Search)
                || category.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return Result<IReadOnlyList<CatalogCategory>>.Success(categories);
    }

    private static bool MatchesRegion(RegionCode categoryRegion, RegionCode? requestedRegion, bool includeGlobal)
    {
        if (requestedRegion is not { } region)
        {
            return true;
        }

        if (includeGlobal && region is RegionCode.USA or RegionCode.MX or RegionCode.PA or RegionCode.CO)
        {
            return categoryRegion == region || categoryRegion == RegionCode.GLOBAL;
        }

        return categoryRegion == region;
    }
}
