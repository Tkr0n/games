using FluentValidation;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Domain.Catalog;
using VerkkuGames.Domain.Common;

namespace VerkkuGames.Application.Catalog.Queries;

public sealed record GetItemPriceQuery(Guid ProviderUid, string ItemId);

public sealed class GetItemPriceQueryValidator : AbstractValidator<GetItemPriceQuery>
{
    private const string ExternalIdPattern = "^[A-Za-z0-9_-]+$";

    public GetItemPriceQueryValidator()
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

public sealed class GetItemPriceQueryHandler(
    ICatalogProviderRegistry providerRegistry,
    IValidator<GetItemPriceQuery> validator)
    : IQueryHandler<GetItemPriceQuery, ItemPriceQuote>
{
    public async Task<Result<ItemPriceQuote>> HandleAsync(
        GetItemPriceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<ItemPriceQuote>.Failure(
                Error.Validation(
                    "validation_failed",
                    string.Join("; ", validation.Errors.Select(error => error.ErrorMessage))));
        }

        var providerResult = providerRegistry.GetByUid(query.ProviderUid);
        if (providerResult.IsFailure)
        {
            return Result<ItemPriceQuote>.Failure(providerResult.Error);
        }

        return await providerResult.Value.GetItemPriceAsync(query.ItemId, cancellationToken);
    }
}
