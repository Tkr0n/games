using Microsoft.Extensions.DependencyInjection;
using VerkkuGames.Application;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Application.Catalog.Queries;
using VerkkuGames.Application.Common;
using VerkkuGames.Application.Rates.Queries;
using VerkkuGames.Application.Services;
using VerkkuGames.Domain.Catalog;
using VerkkuGames.Domain.Common;
using VerkkuGames.Domain.Pricing;
using VerkkuGames.Domain.ValueObjects;
using Xunit;

namespace VerkkuGames.UnitTests.Application;

public sealed class CatalogQueriesTests
{
    private static readonly Guid DefaultProviderUid = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");
    private static readonly Guid UnknownProviderUid = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public async Task GetBigCategories_ReturnsProviderBigCategories()
    {
        var provider = new FakeCatalogProvider
        {
            BigCategories =
            [
                new CatalogBigCategory(DefaultProviderUid, "games", "Games", "Game cards", null),
            ],
        };
        var handler = new GetBigCategoriesQueryHandler(new CatalogProviderRegistry([provider]), new GetBigCategoriesQueryValidator());

        var result = await handler.HandleAsync(new GetBigCategoriesQuery(DefaultProviderUid));

        Assert.True(result.IsSuccess);
        var bigCategory = Assert.Single(result.Value);
        Assert.Equal("games", bigCategory.ExternalId);
    }

    [Fact]
    public async Task GetCategories_RegionNull_ReturnsAllRegions()
    {
        var provider = new FakeCatalogProvider
        {
            Categories =
            [
                CreateCategory("cat-usa", "Gift Card USA", RegionCode.USA),
                CreateCategory("cat-mx", "Gift Card Mexico", RegionCode.MX),
                CreateCategory("cat-global", "Gift Card Global", RegionCode.GLOBAL),
            ],
        };
        var handler = CreateCategoriesHandler(provider);

        var result = await handler.HandleAsync(new GetCategoriesQuery());

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
    }

    [Fact]
    public async Task GetCategories_RegionSpecifiedWithoutGlobal_ReturnsExactRegionOnly()
    {
        var provider = new FakeCatalogProvider
        {
            Categories =
            [
                CreateCategory("cat-usa", "Gift Card USA", RegionCode.USA),
                CreateCategory("cat-global", "Gift Card Global", RegionCode.GLOBAL),
                CreateCategory("cat-mx", "Gift Card Mexico", RegionCode.MX),
            ],
        };
        var handler = CreateCategoriesHandler(provider);

        var result = await handler.HandleAsync(new GetCategoriesQuery(Region: RegionCode.USA));

        Assert.True(result.IsSuccess);
        var category = Assert.Single(result.Value);
        Assert.Equal("cat-usa", category.ExternalId);
    }

    [Fact]
    public async Task GetCategories_RegionSpecifiedWithGlobal_IncludesGlobalRegion()
    {
        var provider = new FakeCatalogProvider
        {
            Categories =
            [
                CreateCategory("cat-usa", "Gift Card USA", RegionCode.USA),
                CreateCategory("cat-global", "Gift Card Global", RegionCode.GLOBAL),
                CreateCategory("cat-mx", "Gift Card Mexico", RegionCode.MX),
            ],
        };
        var handler = CreateCategoriesHandler(provider);

        var result = await handler.HandleAsync(new GetCategoriesQuery(Region: RegionCode.USA, IncludeGlobal: true));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains(result.Value, category => category.ExternalId == "cat-usa");
        Assert.Contains(result.Value, category => category.ExternalId == "cat-global");
    }

    [Fact]
    public async Task GetCategories_IncludeGlobalIgnoredForOtherRegions()
    {
        var provider = new FakeCatalogProvider
        {
            Categories =
            [
                CreateCategory("cat-usa", "Gift Card USA", RegionCode.USA),
                CreateCategory("cat-global", "Gift Card Global", RegionCode.GLOBAL),
            ],
        };
        var handler = CreateCategoriesHandler(provider);

        var result = await handler.HandleAsync(new GetCategoriesQuery(Region: RegionCode.OTHER, IncludeGlobal: true));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task GetCategories_FiltersByBigCategoryAndSearchCaseInsensitively()
    {
        var provider = new FakeCatalogProvider
        {
            Categories =
            [
                CreateCategory("cat-1", "PlayStation Store Gift Card USA", RegionCode.USA, bigCategoryId: "games"),
                CreateCategory("cat-2", "Xbox Gift Card USA", RegionCode.USA, bigCategoryId: "games"),
                CreateCategory("cat-3", "PlayStation Gift Card Mexico", RegionCode.MX, bigCategoryId: "other"),
            ],
        };
        var handler = CreateCategoriesHandler(provider);

        var result = await handler.HandleAsync(new GetCategoriesQuery(BigCategoryId: "games", Search: "playstation"));

        Assert.True(result.IsSuccess);
        var category = Assert.Single(result.Value);
        Assert.Equal("cat-1", category.ExternalId);
    }

    [Fact]
    public async Task GetCatalogItems_RegionNull_ReturnsAllRegions()
    {
        var provider = CreateItemsProvider();
        var handler = CreateItemsHandler(provider);

        var result = await handler.HandleAsync(new GetCatalogItemsQuery());

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.TotalCount);
    }

    [Fact]
    public async Task GetCatalogItems_ExactRegionFilter_ExcludesGlobal()
    {
        var provider = CreateItemsProvider();
        var handler = CreateItemsHandler(provider);

        var result = await handler.HandleAsync(new GetCatalogItemsQuery(Region: RegionCode.USA));

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal("sku-usa", item.ExternalId);
    }

    [Fact]
    public async Task GetCatalogItems_InclusiveRegionFilter_ReturnsRegionAndGlobal()
    {
        var provider = CreateItemsProvider();
        var handler = CreateItemsHandler(provider);

        var result = await handler.HandleAsync(new GetCatalogItemsQuery(Region: RegionCode.USA, IncludeGlobal: true));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Contains(result.Value.Items, item => item.ExternalId == "sku-global");
    }

    [Fact]
    public async Task GetCatalogItems_FiltersByProductType()
    {
        var provider = CreateItemsProvider();
        var handler = CreateItemsHandler(provider);

        var result = await handler.HandleAsync(new GetCatalogItemsQuery(ProductType: ProductType.TopUp));

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal("sku-topup", item.ExternalId);
    }

    [Fact]
    public async Task GetCatalogItems_InStockOnly_ExcludesOutOfStockItems()
    {
        var provider = CreateItemsProvider();
        var handler = CreateItemsHandler(provider);

        var result = await handler.HandleAsync(new GetCatalogItemsQuery(InStockOnly: true));

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(result.Value.Items, item => item.ExternalId == "sku-out");
        Assert.Equal(3, result.Value.TotalCount);
    }

    [Fact]
    public async Task GetCatalogItems_SearchIsCaseInsensitive()
    {
        var provider = CreateItemsProvider();
        var handler = CreateItemsHandler(provider);

        var result = await handler.HandleAsync(new GetCatalogItemsQuery(Search: "PLAYSTATION"));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.All(result.Value.Items, item => Assert.Contains("PlayStation", item.Name, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetCatalogItems_PaginatesWithAccurateTotals()
    {
        var provider = CreateItemsProvider();
        var handler = CreateItemsHandler(provider);

        var result = await handler.HandleAsync(new GetCatalogItemsQuery(Page: 2, PageSize: 3));

        Assert.True(result.IsSuccess);
        var page = result.Value;
        Assert.Equal(4, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(2, page.Page);
        Assert.Equal(3, page.PageSize);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task GetCatalogItems_InvalidPaging_ReturnsValidationFailure()
    {
        var provider = CreateItemsProvider();
        var handler = CreateItemsHandler(provider);

        var result = await handler.HandleAsync(new GetCatalogItemsQuery(Page: 0, PageSize: 500));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("validation_failed", result.Error.Code);
    }

    [Fact]
    public async Task GetAll_UnknownProvider_ReturnsNotFoundWithoutLeakingInternalKey()
    {
        var provider = new FakeCatalogProvider();
        var handler = CreateItemsHandler(provider);

        var result = await handler.HandleAsync(new GetCatalogItemsQuery(ProviderUid: UnknownProviderUid));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("provider_not_found", result.Error.Code);
        Assert.DoesNotContain(FakeCatalogProvider.SecretInternalKey, result.Error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_UnknownProvider_ReturnsNotFound()
    {
        var registry = new CatalogProviderRegistry([new FakeCatalogProvider()]);

        var result = registry.GetByUid(UnknownProviderUid);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public void Registry_ReturnsRegisteredProviderByUidAndDefault()
    {
        var provider = new FakeCatalogProvider();
        var registry = new CatalogProviderRegistry([provider]);

        var byUid = registry.GetByUid(DefaultProviderUid);
        var byDefault = registry.GetDefault();

        Assert.True(byUid.IsSuccess);
        Assert.Same(provider, byUid.Value);
        Assert.True(byDefault.IsSuccess);
        Assert.Same(provider, byDefault.Value);
        Assert.Single(registry.GetAll());
    }

    [Fact]
    public void Registry_NoProviders_DefaultReturnsProviderUnavailable()
    {
        var registry = new CatalogProviderRegistry([]);

        var result = registry.GetDefault();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.ProviderUnavailable, result.Error.Type);
        Assert.Equal("no_catalog_provider", result.Error.Code);
        Assert.Empty(registry.GetAll());
    }

    [Fact]
    public async Task GetCatalogItemDetail_ReturnsItemWithRequirementsSiblingsAndRelatedRegions()
    {
        var provider = new FakeCatalogProvider
        {
            Categories =
            [
                CreateCategory("cat-usa", "Gift Card USA", RegionCode.USA),
                CreateCategory("cat-mx", "Gift Card Mexico", RegionCode.MX),
                CreateCategory("cat-other", "Discord Nitro USA", RegionCode.USA),
            ],
            Items =
            [
                CreateItem("sku-a", "cat-usa", "Gift Card USA", RegionCode.USA, priceUsd: 10m),
                CreateItem("sku-b", "cat-usa", "Gift Card USA", RegionCode.USA, priceUsd: 5m),
                CreateItem("sku-c", "cat-mx", "Gift Card Mexico", RegionCode.MX, priceUsd: 10m),
                CreateItem("sku-d", "cat-other", "Discord Nitro USA", RegionCode.USA, priceUsd: 3m),
            ],
            CategoryRequirements = new Dictionary<string, IReadOnlyList<CategoryRequirement>>(StringComparer.Ordinal)
            {
                ["cat-usa"] =
                [
                    new CategoryRequirement("PlayerId", RequirementDataType.Text, "Steam ID", [], 1, 64),
                    new CategoryRequirement("Server", RequirementDataType.SingleSelect, null, ["EU", "NA"], null, null),
                ],
            },
        };
        var handler = CreateDetailHandler(provider);

        var result = await handler.HandleAsync(new GetCatalogItemDetailQuery(DefaultProviderUid, "sku-a"));

        Assert.True(result.IsSuccess);
        var detail = result.Value;
        Assert.Equal("sku-a", detail.Item.ExternalId);
        Assert.Equal(2, detail.Requirements.Count);
        var sibling = Assert.Single(detail.SiblingItems);
        Assert.Equal("sku-b", sibling.ExternalId);
        var related = Assert.Single(detail.RelatedRegionCategories);
        Assert.Equal("cat-mx", related.ExternalId);
    }

    [Fact]
    public async Task GetCatalogItemDetail_OrdersSiblingsByPriceThenName()
    {
        var provider = new FakeCatalogProvider
        {
            Items =
            [
                CreateItem("sku-pricey", "cat-usa", "Gift Card USA", RegionCode.USA, priceUsd: 50m, name: "Alpha"),
                CreateItem("sku-zeta", "cat-usa", "Gift Card USA", RegionCode.USA, priceUsd: 5m, name: "Zeta"),
                CreateItem("sku-alpha", "cat-usa", "Gift Card USA", RegionCode.USA, priceUsd: 5m, name: "Alpha"),
                CreateItem("sku-target", "cat-usa", "Gift Card USA", RegionCode.USA, priceUsd: 20m, name: "Target"),
            ],
        };
        var handler = CreateDetailHandler(provider);

        var result = await handler.HandleAsync(new GetCatalogItemDetailQuery(DefaultProviderUid, "sku-target"));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["sku-alpha", "sku-zeta", "sku-pricey"],
            result.Value.SiblingItems.Select(item => item.ExternalId));
    }

    [Fact]
    public async Task GetCatalogItemDetail_ItemMissing_ReturnsNotFound()
    {
        var provider = new FakeCatalogProvider
        {
            Items = [CreateItem("sku-a", "cat-usa", "Gift Card USA", RegionCode.USA)],
        };
        var handler = CreateDetailHandler(provider);

        var result = await handler.HandleAsync(new GetCatalogItemDetailQuery(DefaultProviderUid, "missing"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("item_not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetCatalogItemDetail_RequirementsFailure_DegradesToEmptyList()
    {
        var provider = new FakeCatalogProvider
        {
            Items = [CreateItem("sku-a", "cat-usa", "Gift Card USA", RegionCode.USA)],
            RequirementsError = Error.ProviderUnavailable("provider_unavailable", "The catalog provider is unavailable."),
        };
        var handler = CreateDetailHandler(provider);

        var result = await handler.HandleAsync(new GetCatalogItemDetailQuery(DefaultProviderUid, "sku-a"));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Requirements);
    }

    [Fact]
    public async Task GetCatalogItemDetail_CategoriesFailure_DegradesToEmptyRelatedRegions()
    {
        var provider = new FakeCatalogProvider
        {
            Items = [CreateItem("sku-a", "cat-usa", "Gift Card USA", RegionCode.USA)],
            CategoriesError = Error.ProviderUnavailable("provider_unavailable", "The catalog provider is unavailable."),
        };
        var handler = CreateDetailHandler(provider);

        var result = await handler.HandleAsync(new GetCatalogItemDetailQuery(DefaultProviderUid, "sku-a"));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.RelatedRegionCategories);
    }

    [Fact]
    public async Task GetItemPrice_ReturnsProviderQuote()
    {
        var quotedAt = DateTimeOffset.UtcNow;
        var provider = new FakeCatalogProvider
        {
            PriceQuotes = new Dictionary<string, ItemPriceQuote>(StringComparer.Ordinal)
            {
                ["sku-a"] = new ItemPriceQuote(DefaultProviderUid, "sku-a", "Gift Card 10", Money.Create(9.99m, CurrencyCode.USD), true, quotedAt),
            },
        };
        var handler = new GetItemPriceQueryHandler(new CatalogProviderRegistry([provider]), new GetItemPriceQueryValidator());

        var result = await handler.HandleAsync(new GetItemPriceQuery(DefaultProviderUid, "sku-a"));

        Assert.True(result.IsSuccess);
        Assert.Equal(9.99m, result.Value.RetailPriceUsd.Amount);
        Assert.Equal(quotedAt, result.Value.QuotedAtUtc);
    }

    [Fact]
    public async Task GetItemPrice_UnknownItem_ReturnsNotFound()
    {
        var provider = new FakeCatalogProvider();
        var handler = new GetItemPriceQueryHandler(new CatalogProviderRegistry([provider]), new GetItemPriceQueryValidator());

        var result = await handler.HandleAsync(new GetItemPriceQuery(DefaultProviderUid, "missing"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task GetCategoryRequirements_ReturnsRequirementsList()
    {
        var provider = new FakeCatalogProvider
        {
            CategoryRequirements = new Dictionary<string, IReadOnlyList<CategoryRequirement>>(StringComparer.Ordinal)
            {
                ["cat-usa"] = [new CategoryRequirement("PlayerId", RequirementDataType.Text, "Steam ID", [], 1, 64)],
            },
        };
        var handler = new GetCategoryRequirementsQueryHandler(new CatalogProviderRegistry([provider]), new GetCategoryRequirementsQueryValidator());

        var result = await handler.HandleAsync(new GetCategoryRequirementsQuery(DefaultProviderUid, "cat-usa"));

        Assert.True(result.IsSuccess);
        var requirement = Assert.Single(result.Value);
        Assert.Equal("PlayerId", requirement.FieldName);
    }

    [Fact]
    public async Task GetCategoryRequirements_UnknownCategory_ReturnsEmptyList()
    {
        var provider = new FakeCatalogProvider();
        var handler = new GetCategoryRequirementsQueryHandler(new CatalogProviderRegistry([provider]), new GetCategoryRequirementsQueryValidator());

        var result = await handler.HandleAsync(new GetCategoryRequirementsQuery(DefaultProviderUid, "cat-none"));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task GetExchangeRates_ReturnsSnapshotFromProvider()
    {
        var snapshot = new ExchangeRatesSnapshot(
            CurrencyCode.USD,
            new Dictionary<CurrencyCode, decimal>
            {
                [CurrencyCode.BTC] = 0.0000105m,
                [CurrencyCode.ETH] = 0.00028m,
            },
            DateTimeOffset.UtcNow);
        var handler = new GetExchangeRatesQueryHandler(new FakeExchangeRateProvider(Result<ExchangeRatesSnapshot>.Success(snapshot)));

        var result = await handler.HandleAsync(new GetExchangeRatesQuery());

        Assert.True(result.IsSuccess);
        Assert.Equal(CurrencyCode.USD, result.Value.BaseCurrency);
        Assert.Equal(2, result.Value.RatesPerUsd.Count);
    }

    [Fact]
    public async Task GetExchangeRates_ProviderFailure_PropagatesError()
    {
        var error = Error.RateLimited("rate_limited", "Too many requests.", 30);
        var handler = new GetExchangeRatesQueryHandler(new FakeExchangeRateProvider(Result<ExchangeRatesSnapshot>.Failure(error)));

        var result = await handler.HandleAsync(new GetExchangeRatesQuery());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.RateLimited, result.Error.Type);
        Assert.Equal(30, result.Error.RetryAfterSeconds);
    }

    [Fact]
    public void AddApplication_RegistersAllQueryHandlersAndServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICatalogProvider>(new FakeCatalogProvider());
        services.AddSingleton<IExchangeRateProvider>(
            new FakeExchangeRateProvider(Result<ExchangeRatesSnapshot>.Failure(
                Error.ProviderUnavailable("provider_unavailable", "The rates provider is unavailable."))));

        services.AddApplication();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider;

        Assert.NotNull(scoped.GetService<IPricingService>());
        Assert.NotNull(scoped.GetService<IRegionResolver>());
        Assert.NotNull(scoped.GetService<ICatalogProviderRegistry>());
        Assert.NotNull(scoped.GetService<IQueryHandler<GetBigCategoriesQuery, IReadOnlyList<CatalogBigCategory>>>());
        Assert.NotNull(scoped.GetService<IQueryHandler<GetCategoriesQuery, IReadOnlyList<CatalogCategory>>>());
        Assert.NotNull(scoped.GetService<IQueryHandler<GetCatalogItemsQuery, PagedResult<CatalogItem>>>());
        Assert.NotNull(scoped.GetService<IQueryHandler<GetCatalogItemDetailQuery, CatalogItemDetail>>());
        Assert.NotNull(scoped.GetService<IQueryHandler<GetItemPriceQuery, ItemPriceQuote>>());
        Assert.NotNull(scoped.GetService<IQueryHandler<GetCategoryRequirementsQuery, IReadOnlyList<CategoryRequirement>>>());
        Assert.NotNull(scoped.GetService<IQueryHandler<GetExchangeRatesQuery, ExchangeRatesSnapshot>>());
    }

    private static GetCategoriesQueryHandler CreateCategoriesHandler(FakeCatalogProvider provider) =>
        new(new CatalogProviderRegistry([provider]), new GetCategoriesQueryValidator());

    private static GetCatalogItemsQueryHandler CreateItemsHandler(FakeCatalogProvider provider) =>
        new(new CatalogProviderRegistry([provider]), new GetCatalogItemsQueryValidator());

    private static GetCatalogItemDetailQueryHandler CreateDetailHandler(FakeCatalogProvider provider) =>
        new(new CatalogProviderRegistry([provider]), new GetCatalogItemDetailQueryValidator(), new RegionResolver());

    private static FakeCatalogProvider CreateItemsProvider() =>
        new()
        {
            Items =
            [
                CreateItem("sku-usa", "cat-usa", "Gift Card USA", RegionCode.USA, priceUsd: 10m, name: "PlayStation Store Gift Card USA"),
                CreateItem("sku-global", "cat-global", "Gift Card Global", RegionCode.GLOBAL, priceUsd: 12m, name: "PlayStation Store Gift Card Global"),
                CreateItem("sku-mx", "cat-mx", "Gift Card Mexico", RegionCode.MX, priceUsd: 11m, name: "Xbox Gift Card Mexico"),
                CreateItem("sku-topup", "cat-usa", "Gift Card USA", RegionCode.CO, productType: ProductType.TopUp, priceUsd: 7m, name: "Nintendo Top Up USA", inStock: false),
            ],
        };

    private static CatalogCategory CreateCategory(
        string externalId,
        string name,
        RegionCode region,
        string bigCategoryId = "games") =>
        new(DefaultProviderUid, externalId, bigCategoryId, name, "Description", null, region);

    private static CatalogItem CreateItem(
        string externalId,
        string categoryId,
        string categoryName,
        RegionCode region,
        ProductType productType = ProductType.Voucher,
        decimal? priceUsd = 10m,
        bool inStock = true,
        string? name = null) =>
        new(
            DefaultProviderUid,
            externalId,
            categoryId,
            categoryName,
            name ?? $"{categoryName} {externalId}",
            "Description",
            null,
            productType,
            region,
            priceUsd is null ? null : Money.Create(priceUsd.Value, CurrencyCode.USD),
            inStock,
            false);

    private sealed class FakeExchangeRateProvider(Result<ExchangeRatesSnapshot> result) : IExchangeRateProvider
    {
        public Task<Result<ExchangeRatesSnapshot>> GetCryptoRatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FakeCatalogProvider : ICatalogProvider
    {
        public const string SecretInternalKey = "secret-internal-provider-key";

        public string InternalKey => SecretInternalKey;

        public Guid ProviderUid { get; init; } = DefaultProviderUid;

        public IReadOnlyList<CatalogBigCategory> BigCategories { get; init; } = [];

        public IReadOnlyList<CatalogCategory> Categories { get; init; } = [];

        public IReadOnlyList<CatalogItem> Items { get; init; } = [];

        public Dictionary<string, IReadOnlyList<CategoryRequirement>> CategoryRequirements { get; init; } =
            new(StringComparer.Ordinal);

        public Dictionary<string, ItemPriceQuote> PriceQuotes { get; init; } =
            new(StringComparer.Ordinal);

        public Error? RequirementsError { get; init; }

        public Error? CategoriesError { get; init; }

        public Task<Result<IReadOnlyList<CatalogBigCategory>>> GetBigCategoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<CatalogBigCategory>>.Success(BigCategories));

        public Task<Result<IReadOnlyList<CatalogCategory>>> GetCategoriesAsync(
            string? bigCategoryId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CategoriesError is { } error
                ? Result<IReadOnlyList<CatalogCategory>>.Failure(error)
                : Result<IReadOnlyList<CatalogCategory>>.Success(Categories));

        public Task<Result<IReadOnlyList<CatalogItem>>> GetItemsAsync(
            string? categoryId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<CatalogItem>>.Success(Items));

        public Task<Result<ItemPriceQuote>> GetItemPriceAsync(
            string itemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PriceQuotes.TryGetValue(itemId, out var quote)
                ? Result<ItemPriceQuote>.Success(quote)
                : Result<ItemPriceQuote>.Failure(Error.NotFound("item_not_found", "The requested catalog item was not found.")));

        public Task<Result<IReadOnlyList<CategoryRequirement>>> GetCategoryRequirementsAsync(
            string categoryId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RequirementsError is { } error
                ? Result<IReadOnlyList<CategoryRequirement>>.Failure(error)
                : Result<IReadOnlyList<CategoryRequirement>>.Success(
                    CategoryRequirements.TryGetValue(categoryId, out var requirements) ? requirements : []));

        public Task<Result<bool>> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<bool>.Success(true));
    }
}
