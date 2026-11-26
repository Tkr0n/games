using FluentValidation;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Domain.Catalog;
using VerkkuGames.Domain.Common;

namespace VerkkuGames.Application.Catalog.Queries;

public sealed record GetCatalogItemDetailQuery(Guid ProviderUid, string ItemId);

public sealed class GetCatalogItemDetailQueryValidator : AbstractValidator<GetCatalogItemDetailQuery>
{
    private const string ExternalIdPattern = "^[A-Za-z0-9_-]+$";

    public GetCatalogItemDetailQueryValidator()
    {
        RuleFor(query => query.ProviderUid)
            .NotEqual(Guid.Empty)
            .WithMessage("'Provider Uid' must not be an empty identifier.");

        RuleFor(query => query.ItemId)
            .NotEmpty()
            .MaximumLength(64)
            .Matches(ExternalIdPattern);
    }
}

public sealed class GetCatalogItemDetailQueryHandler(
    ICatalogProviderRegistry providerRegistry,
    IValidator<GetCatalogItemDetailQuery> validator,
    IRegionResolver regionResolver)
    : IQueryHandler<GetCatalogItemDetailQuery, CatalogItemDetail>
{
    public async Task<Result<CatalogItemDetail>> HandleAsync(
        GetCatalogItemDetailQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<CatalogItemDetail>.Failure(
                Error.Validation(
                    "validation_failed",
                    string.Join("; ", validation.Errors.Select(error => error.ErrorMessage))));
        }

        var providerResult = providerRegistry.GetByUid(query.ProviderUid);
        if (providerResult.IsFailure)
        {
            return Result<CatalogItemDetail>.Failure(providerResult.Error);
        }

        var provider = providerResult.Value;

        var itemResult = await provider.GetItemByIdAsync(query.ItemId, cancellationToken).ConfigureAwait(false);
        if (itemResult.IsFailure)
        {
            return Result<CatalogItemDetail>.Failure(itemResult.Error);
        }

        var item = itemResult.Value;

        IReadOnlyList<CategoryRequirement> requirements = [];
        var requirementsResult = await provider.GetCategoryRequirementsAsync(item.CategoryId, cancellationToken);
        if (requirementsResult.IsSuccess)
        {
            requirements = requirementsResult.Value;
        }

        IReadOnlyList<CatalogItem> siblingItems = [];
        var siblingsResult = await provider.GetItemsAsync(item.CategoryId, cancellationToken).ConfigureAwait(false);
        if (siblingsResult.IsSuccess)
        {
            siblingItems = siblingsResult.Value
                .Where(candidate => string.Equals(candidate.CategoryId, item.CategoryId, StringComparison.Ordinal))
                .Where(candidate => !string.Equals(candidate.ExternalId, item.ExternalId, StringComparison.Ordinal))
                .OrderBy(candidate => candidate.RetailPriceUsd?.Amount)
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.ExternalId, StringComparer.Ordinal)
                .ToArray();
        }

        IReadOnlyList<CatalogCategory> relatedRegionCategories = [];
        var categoriesResult = await provider.GetCategoriesAsync(bigCategoryId: null, cancellationToken);
        if (categoriesResult.IsSuccess)
        {
            var baseTitle = regionResolver.ExtractBaseTitle(item.CategoryName);
            if (baseTitle.Length > 0)
            {
                relatedRegionCategories = categoriesResult.Value
                    .Where(category => !string.Equals(category.ExternalId, item.CategoryId, StringComparison.Ordinal))
                    .Where(category => string.Equals(
                        regionResolver.ExtractBaseTitle(category.Name),
                        baseTitle,
                        StringComparison.Ordinal))
                    .ToArray();
            }
        }

        return Result<CatalogItemDetail>.Success(
            new CatalogItemDetail(item, requirements, siblingItems, relatedRegionCategories));
    }
}
