using FluentValidation;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Domain.Catalog;
using VerkkuGames.Domain.Common;

namespace VerkkuGames.Application.Catalog.Queries;

public sealed record GetBigCategoriesQuery(Guid? ProviderUid = null);

public sealed class GetBigCategoriesQueryValidator : AbstractValidator<GetBigCategoriesQuery>
{
    public GetBigCategoriesQueryValidator()
    {
        RuleFor(query => query.ProviderUid)
            .NotEqual(Guid.Empty)
            .When(query => query.ProviderUid.HasValue)
            .WithMessage("'Provider Uid' must not be an empty identifier.");
    }
}

public sealed class GetBigCategoriesQueryHandler(
    ICatalogProviderRegistry providerRegistry,
    IValidator<GetBigCategoriesQuery> validator)
    : IQueryHandler<GetBigCategoriesQuery, IReadOnlyList<CatalogBigCategory>>
{
    public async Task<Result<IReadOnlyList<CatalogBigCategory>>> HandleAsync(
        GetBigCategoriesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<IReadOnlyList<CatalogBigCategory>>.Failure(
                Error.Validation(
                    "validation_failed",
                    string.Join("; ", validation.Errors.Select(error => error.ErrorMessage))));
        }

        var providerResult = query.ProviderUid is { } providerUid
            ? providerRegistry.GetByUid(providerUid)
            : providerRegistry.GetDefault();

        if (providerResult.IsFailure)
        {
            return Result<IReadOnlyList<CatalogBigCategory>>.Failure(providerResult.Error);
        }

        return await providerResult.Value.GetBigCategoriesAsync(cancellationToken);
    }
}
