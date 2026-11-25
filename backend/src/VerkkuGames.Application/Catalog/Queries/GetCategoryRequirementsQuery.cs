using FluentValidation;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Domain.Catalog;
using VerkkuGames.Domain.Common;

namespace VerkkuGames.Application.Catalog.Queries;

public sealed record GetCategoryRequirementsQuery(Guid ProviderUid, string CategoryId);

public sealed class GetCategoryRequirementsQueryValidator : AbstractValidator<GetCategoryRequirementsQuery>
{
    private const string ExternalIdPattern = "^[A-Za-z0-9_-]+$";

    public GetCategoryRequirementsQueryValidator()
    {
        RuleFor(query => query.ProviderUid)
            .NotEqual(Guid.Empty)
            .WithMessage("'Provider Uid' must not be an empty identifier.");

        RuleFor(query => query.CategoryId)
            .NotEmpty()
            .MaximumLength(64)
            .Matches(ExternalIdPattern);
    }
}

public sealed class GetCategoryRequirementsQueryHandler(
    ICatalogProviderRegistry providerRegistry,
    IValidator<GetCategoryRequirementsQuery> validator)
    : IQueryHandler<GetCategoryRequirementsQuery, IReadOnlyList<CategoryRequirement>>
{
    public async Task<Result<IReadOnlyList<CategoryRequirement>>> HandleAsync(
        GetCategoryRequirementsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<IReadOnlyList<CategoryRequirement>>.Failure(
                Error.Validation(
                    "validation_failed",
                    string.Join("; ", validation.Errors.Select(error => error.ErrorMessage))));
        }

        var providerResult = providerRegistry.GetByUid(query.ProviderUid);
        if (providerResult.IsFailure)
        {
            return Result<IReadOnlyList<CategoryRequirement>>.Failure(providerResult.Error);
        }

        return await providerResult.Value.GetCategoryRequirementsAsync(query.CategoryId, cancellationToken);
    }
}
