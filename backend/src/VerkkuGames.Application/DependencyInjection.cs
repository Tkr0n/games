using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Application.Catalog.Queries;
using VerkkuGames.Application.Common;
using VerkkuGames.Application.Options;
using VerkkuGames.Application.Rates.Queries;
using VerkkuGames.Application.Services;
using VerkkuGames.Domain.Catalog;
using VerkkuGames.Domain.Pricing;

namespace VerkkuGames.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<PricingOptions>()
            .ValidateDataAnnotations();

        services.AddSingleton<IPricingService, PricingService>();
        services.AddSingleton<IRegionResolver, RegionResolver>();
        services.AddScoped<ICatalogProviderRegistry, CatalogProviderRegistry>();

        services.AddScoped<
            IQueryHandler<GetBigCategoriesQuery, IReadOnlyList<CatalogBigCategory>>,
            GetBigCategoriesQueryHandler>();
        services.AddScoped<
            IQueryHandler<GetCategoriesQuery, IReadOnlyList<CatalogCategory>>,
            GetCategoriesQueryHandler>();
        services.AddScoped<
            IQueryHandler<GetCatalogItemsQuery, PagedResult<CatalogItem>>,
            GetCatalogItemsQueryHandler>();
        services.AddScoped<
            IQueryHandler<GetCatalogItemDetailQuery, CatalogItemDetail>,
            GetCatalogItemDetailQueryHandler>();
        services.AddScoped<
            IQueryHandler<GetItemPriceQuery, ItemPriceQuote>,
            GetItemPriceQueryHandler>();
        services.AddScoped<
            IQueryHandler<GetCategoryRequirementsQuery, IReadOnlyList<CategoryRequirement>>,
            GetCategoryRequirementsQueryHandler>();
        services.AddScoped<
            IQueryHandler<GetExchangeRatesQuery, ExchangeRatesSnapshot>,
            GetExchangeRatesQueryHandler>();

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}
