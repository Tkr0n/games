using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using VerkkuGames.Application;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Application.Options;
using VerkkuGames.Application.Services;
using VerkkuGames.Domain.Catalog;
using VerkkuGames.Domain.Common;
using VerkkuGames.Domain.ValueObjects;
using VerkkuGames.Infrastructure;
using VerkkuGames.Infrastructure.Caching;
using VerkkuGames.Infrastructure.Common;
using VerkkuGames.Infrastructure.Options;
using VerkkuGames.Infrastructure.Providers.Binance;
using VerkkuGames.Infrastructure.Providers.Shop2TopUp;
using Xunit;

namespace VerkkuGames.UnitTests.Infrastructure;

public sealed class Shop2TopUpCatalogProviderTests
{
    private const string BaseUrl = "https://catalog.test/api/endpoints/v1";
    private const string BasePath = "/api/endpoints/v1";
    private const string KeyId = "test-key-id";
    private const string KeySecret = "test-key-secret";
    private const string AuthorizationHeader = "Bearer test-key-id.test-key-secret";
    private const decimal DefaultMarkupPercent = 10m;
    private const decimal MinimumMarginUsd = 0.25m;

    private static readonly Guid ProviderUid = Guid.Parse("d4f8a2c1-7b3e-4f90-9a12-6e8b5c3d1f42");

    // ---------------------------------------------------------------- options

    [Fact]
    public void ProviderUid_And_InternalKey_AreConfigured()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, BigCategoriesJson));

        Assert.Equal(ProviderUid, provider.ProviderUid);
        Assert.False(string.IsNullOrWhiteSpace(provider.InternalKey));
        Assert.Equal(provider.InternalKey.Trim(), provider.InternalKey);
        Assert.DoesNotContain(" ", provider.InternalKey, StringComparison.Ordinal);
    }

    // -------------------------------------------------------- big categories

    [Fact]
    public async Task GetBigCategories_SendsBearerAuthorizationAndUiFilteredEndpoint()
    {
        var (provider, handler) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, BigCategoriesJson));

        var result = await provider.GetBigCategoriesAsync();

        Assert.True(result.IsSuccess);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"{BaseUrl}/catalog/big-categories?for_ui=true", request.Url);
        Assert.Equal(AuthorizationHeader, request.Authorization);
    }

    [Fact]
    public async Task GetBigCategories_MapsIdsAndRejectsUnsafeImages()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, BigCategoriesJson));

        var result = await provider.GetBigCategoriesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);

        var numeric = result.Value[0];
        Assert.Equal("1", numeric.ExternalId);
        Assert.Equal("Game Cards", numeric.Name);
        Assert.Equal("Digital game cards", numeric.Description);
        Assert.Equal("https://cdn.test/big.png", numeric.ImageUrl);
        Assert.Equal(ProviderUid, numeric.ProviderUid);

        var stringId = result.Value[1];
        Assert.Equal("2", stringId.ExternalId);
        Assert.Null(stringId.ImageUrl);
        Assert.Equal(ProviderUid, stringId.ProviderUid);
    }

    // ------------------------------------------------------------ categories

    [Fact]
    public async Task GetCategories_WithoutFilter_SendsUiEndpointAndSanitizesCategoryImages()
    {
        var (provider, handler) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, CategoriesJson));

        var result = await provider.GetCategoriesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal($"{BaseUrl}/catalog/categories?for_ui=true", Assert.Single(handler.Requests).Url);
        Assert.Equal(2, result.Value.Count);

        var usa = result.Value[0];
        Assert.Equal("12", usa.ExternalId);
        Assert.Equal("1", usa.BigCategoryId);
        Assert.Equal("https://cdn.test/ps-usa.png", usa.ImageUrl);
        Assert.Equal(RegionCode.USA, usa.Region);

        var global = result.Value[1];
        Assert.Equal("13", global.ExternalId);
        Assert.Equal("2", global.BigCategoryId);
        Assert.Null(global.ImageUrl);
        Assert.Equal(RegionCode.GLOBAL, global.Region);
    }

    [Fact]
    public async Task GetCategories_WithBigCategoryFilter_EscapesQueryValue()
    {
        var (provider, handler) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, CategoriesJson));

        var result = await provider.GetCategoriesAsync(bigCategoryId: "1&2");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            $"{BaseUrl}/catalog/categories?for_ui=true&bigCategoryId=1%262",
            Assert.Single(handler.Requests).Url);
    }

    // ------------------------------------------------------- subcategories

    [Fact]
    public async Task GetItems_WithoutFilter_AppliesPricingLocksPricesAndInheritsImages()
    {
        var (provider, handler) = CreateProvider(RouteCatalog());

        var result = await provider.GetItemsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal($"{BaseUrl}/catalog/subcategories", handler.Requests[0].Url);
        Assert.Equal(3, result.Value.Count);

        var playStation = result.Value[0];
        Assert.Equal("999", playStation.ExternalId);
        Assert.Equal("12", playStation.CategoryId);
        Assert.Equal("PlayStation Store Gift Card USA", playStation.CategoryName);
        Assert.Equal("PlayStation 10 USD", playStation.Name);
        Assert.Equal(ProductType.TopUp, playStation.ProductType);
        Assert.Equal(RegionCode.USA, playStation.Region);
        Assert.False(playStation.PricesLocked);
        Assert.True(playStation.InStock);
        Assert.True(playStation.RetailPriceUsd.HasValue);
        Assert.Equal(CurrencyCode.USD, playStation.RetailPriceUsd.Value.Currency);
        // max(0.95 * 1.10, 0.95 + 0.25) => 1.20 retail, never the 0.95 reseller cost.
        Assert.Equal(1.20m, playStation.RetailPriceUsd.Value.Amount);
        Assert.NotEqual(0.95m, playStation.RetailPriceUsd.Value.Amount);
        Assert.Equal("https://cdn.test/ps-usa.png", playStation.ImageUrl);
        Assert.Equal(ProviderUid, playStation.ProviderUid);

        var locked = result.Value[1];
        Assert.Equal("1000", locked.ExternalId);
        Assert.True(locked.PricesLocked);
        Assert.False(locked.RetailPriceUsd.HasValue);
        Assert.False(locked.InStock);
        Assert.Equal(ProductType.Voucher, locked.ProductType);
        Assert.Equal(RegionCode.USA, locked.Region);

        var steam = result.Value[2];
        Assert.Equal("1001", steam.ExternalId);
        Assert.False(steam.PricesLocked);
        Assert.True(steam.RetailPriceUsd.HasValue);
        Assert.Equal(4.40m, steam.RetailPriceUsd.Value.Amount);
        Assert.Equal("https://cdn.test/steam-5.png", steam.ImageUrl);
        Assert.Equal(RegionCode.GLOBAL, steam.Region);
    }

    [Fact]
    public async Task GetItems_WithCategoryFilter_EscapesQueryValue()
    {
        var (provider, handler) = CreateProvider(RouteCatalog());

        var result = await provider.GetItemsAsync(categoryId: "1&2");

        Assert.True(result.IsSuccess);
        Assert.Equal($"{BaseUrl}/catalog/subcategories?categoryId=1%262", handler.Requests[0].Url);
    }

    [Fact]
    public async Task GetItems_RejectsUnsafeItemImages_AndFallsBackToParentImage()
    {
        var (provider, _) = CreateProvider(request =>
        {
            var pathAndQuery = request.RequestUri!.PathAndQuery;
            if (pathAndQuery.StartsWith(BasePath + "/catalog/subcategories", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, UnsafeImageSubcategoriesJson);
            }

            return JsonResponse(HttpStatusCode.OK, UnsafeImageCategoriesJson);
        });

        var result = await provider.GetItemsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);

        // Own image is javascript: -> rejected, valid parent category image inherited.
        Assert.Equal("https://cdn.test/ps-usa.png", result.Value[0].ImageUrl);

        // Own image is http: -> rejected, parent category image is also unsafe -> null.
        Assert.Null(result.Value[1].ImageUrl);

        // No own image and no parent category image -> null.
        Assert.Null(result.Value[2].ImageUrl);
    }

    [Fact]
    public async Task GetItems_PricesLockedWithoutPrice_ReturnsNullPriceAndLockedFlag()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, LockedPriceSubcategoriesJson));

        var result = await provider.GetItemsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.Count);
        Assert.All(result.Value.Take(3), item =>
        {
            Assert.True(item.PricesLocked);
            Assert.False(item.RetailPriceUsd.HasValue);
        });

        // A valid price still yields a retail quote even for an unknown product_type,
        // which falls back to TopUp instead of crashing.
        Assert.False(result.Value[3].PricesLocked);
        Assert.True(result.Value[3].RetailPriceUsd.HasValue);
        Assert.Equal(ProductType.TopUp, result.Value[3].ProductType);
    }

    [Fact]
    public async Task GetItems_WhenCategoryLookupFails_ReturnsItemsWithoutImages()
    {
        var (provider, _) = CreateProvider(request =>
            request.RequestUri!.PathAndQuery.StartsWith(BasePath + "/catalog/subcategories", StringComparison.Ordinal)
                ? JsonResponse(HttpStatusCode.OK, LockedPriceSubcategoriesJson)
                : JsonResponse(HttpStatusCode.InternalServerError, ErrorJson("INTERNAL_ERROR", "upstream exploded")));

        var result = await provider.GetItemsAsync();

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value);
        Assert.All(result.Value, item => Assert.Null(item.ImageUrl));
    }

    // ------------------------------------------------------------- live price

    [Fact]
    public async Task GetItemPrice_AppliesMarkupAndDoesNotLeakResellerCost()
    {
        var (provider, handler) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, PriceJson));

        var before = DateTimeOffset.UtcNow;
        var result = await provider.GetItemPriceAsync("999");
        var after = DateTimeOffset.UtcNow;

        Assert.True(result.IsSuccess);
        Assert.Equal($"{BaseUrl}/catalog/subcategory/999/price", Assert.Single(handler.Requests).Url);

        var quote = result.Value;
        Assert.Equal("999", quote.ExternalItemId);
        Assert.Equal("PlayStation 10 USD", quote.ItemName);
        Assert.Equal(CurrencyCode.USD, quote.RetailPriceUsd.Currency);
        Assert.Equal(1.20m, quote.RetailPriceUsd.Amount);
        Assert.NotEqual(0.95m, quote.RetailPriceUsd.Amount);
        Assert.NotEqual(0.99m, quote.RetailPriceUsd.Amount);
        Assert.True(quote.InStock);
        Assert.Equal(ProviderUid, quote.ProviderUid);
        Assert.InRange(quote.QuotedAtUtc, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public async Task GetItemPrice_InvalidUnitPrice_ReturnsProviderUnavailable()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, PriceWithInvalidUnitPriceJson));

        var result = await provider.GetItemPriceAsync("999");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.ProviderUnavailable, result.Error.Type);
        Assert.Equal("catalog_provider_unavailable", result.Error.Code);
    }

    [Fact]
    public async Task GetItemPrice_ReloadRequired_ReturnsPricesLocked()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(
            HttpStatusCode.Forbidden,
            ErrorJson("RELOAD_REQUIRED", "Contact billing to reload the account balance.")));

        var result = await provider.GetItemPriceAsync("999");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.PricesLocked, result.Error.Type);
        Assert.Equal("prices_locked", result.Error.Code);
        Assert.Equal("Product prices are temporarily locked.", result.Error.Message);
        Assert.Null(result.Error.RetryAfterSeconds);
    }

    [Fact]
    public async Task GetItemPrice_ItemNotFound_ReturnsNotFound()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(
            HttpStatusCode.NotFound,
            ErrorJson("ITEM_NOT_FOUND", "item 999 does not exist")));

        var result = await provider.GetItemPriceAsync("999");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("item_not_found", result.Error.Code);
        Assert.Equal("The requested item was not found.", result.Error.Message);
    }

    [Fact]
    public async Task GetItems_InvalidSubcategory_ReturnsNotFound()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(
            HttpStatusCode.NotFound,
            ErrorJson("INVALID_SUBCATEGORY", "subcategory 123 is not valid")));

        var result = await provider.GetItemsAsync(categoryId: "123");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("item_not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetItemPrice_PricingUpdating_ReturnsConflictWithDefaultRetryAfter()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(
            HttpStatusCode.Conflict,
            ErrorJson("PRICING_UPDATING", "pricing refresh in progress")));

        var result = await provider.GetItemPriceAsync("999");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("pricing_updating", result.Error.Code);
        Assert.Equal("Product pricing is currently being refreshed. Please retry shortly.", result.Error.Message);
        Assert.Equal(30, result.Error.RetryAfterSeconds);
    }

    [Fact]
    public async Task GetItemPrice_PricingUpdating_UsesEnvelopeRetryAfter()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(
            HttpStatusCode.Conflict,
            ErrorJson("PRICING_UPDATING", "pricing refresh in progress", retryAfter: 12)));

        var result = await provider.GetItemPriceAsync("999");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(12, result.Error.RetryAfterSeconds);
    }

    // -------------------------------------------------------------- rate limit

    [Fact]
    public async Task GetBigCategories_RateLimitExceeded_UsesEnvelopeRetryAfter()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(
            HttpStatusCode.TooManyRequests,
            ErrorJson("RATE_LIMIT_EXCEEDED", "too many requests", retryAfter: 17)));

        var result = await provider.GetBigCategoriesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.RateLimited, result.Error.Type);
        Assert.Equal("provider_rate_limited", result.Error.Code);
        Assert.Equal("Catalog service is busy. Please retry shortly.", result.Error.Message);
        Assert.Equal(17, result.Error.RetryAfterSeconds);
    }

    [Fact]
    public async Task GetBigCategories_RateLimitExceeded_UsesRetryAfterHeaderWhenEnvelopeOmitsIt()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(
            HttpStatusCode.TooManyRequests,
            ErrorJson("RATE_LIMIT_EXCEEDED", "too many requests"),
            retryAfterSeconds: 42));

        var result = await provider.GetBigCategoriesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.RateLimited, result.Error.Type);
        Assert.Equal(42, result.Error.RetryAfterSeconds);
    }

    [Fact]
    public async Task GetBigCategories_Bare429WithoutEnvelope_StillReturnsRateLimited()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(
            HttpStatusCode.TooManyRequests,
            "Too many requests",
            retryAfterSeconds: 9));

        var result = await provider.GetBigCategoriesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.RateLimited, result.Error.Type);
        Assert.Equal("provider_rate_limited", result.Error.Code);
        Assert.Equal(9, result.Error.RetryAfterSeconds);
    }

    [Fact]
    public async Task GetItemPrice_Bare409WithoutEnvelope_StillReturnsConflict()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(HttpStatusCode.Conflict, "Conflict"));

        var result = await provider.GetItemPriceAsync("999");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("pricing_updating", result.Error.Code);
        Assert.Equal(30, result.Error.RetryAfterSeconds);
    }

    // ------------------------------------------------------- sanitized errors

    [Theory]
    [InlineData("ACCOUNT_FROZEN", 403)]
    [InlineData("IP_NOT_ALLOWED", 403)]
    [InlineData("INVALID_API_KEY", 401)]
    [InlineData("MISSING_API_KEY", 401)]
    [InlineData("INTERNAL_ERROR", 500)]
    public async Task GetBigCategories_SensitiveOrUnknownErrors_ReturnSanitizedProviderUnavailable(
        string errorCode,
        int statusCode)
    {
        const string rawDetail = "Reseller account 42 frozen; write to billing@wholesale.example with key deadbeef";

        var (provider, _) = CreateProvider(_ => JsonResponse((HttpStatusCode)statusCode, ErrorJson(errorCode, rawDetail)));

        var result = await provider.GetBigCategoriesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.ProviderUnavailable, result.Error.Type);
        Assert.Equal("catalog_provider_unavailable", result.Error.Code);
        Assert.Equal("Catalog service is temporarily unavailable.", result.Error.Message);
        Assert.DoesNotContain(provider.InternalKey, result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(provider.InternalKey, result.Error.Code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(KeyId, result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(KeySecret, result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(errorCode, result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("billing@wholesale.example", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Reseller account", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetBigCategories_NetworkFailure_ReturnsProviderUnavailable()
    {
        var (provider, _) = CreateProvider(_ => throw new HttpRequestException("connection refused"));

        var result = await provider.GetBigCategoriesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.ProviderUnavailable, result.Error.Type);
        Assert.Equal("catalog_provider_unavailable", result.Error.Code);
    }

    [Fact]
    public async Task GetBigCategories_Timeout_ReturnsProviderUnavailable()
    {
        var (provider, _) = CreateProvider(_ => throw new TimeoutRejectedException("attempt timed out"));

        var result = await provider.GetBigCategoriesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.ProviderUnavailable, result.Error.Type);
        Assert.Equal("catalog_provider_unavailable", result.Error.Code);
    }

    [Fact]
    public async Task GetBigCategories_MalformedJson_ReturnsProviderUnavailable()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, "{ this is not json "));

        var result = await provider.GetBigCategoriesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.ProviderUnavailable, result.Error.Type);
    }

    [Fact]
    public async Task GetBigCategories_SuccessFalseWithOkStatus_ReturnsMappedError()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(
            HttpStatusCode.OK,
            ErrorJson("ACCOUNT_FROZEN", "account frozen")));

        var result = await provider.GetBigCategoriesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.ProviderUnavailable, result.Error.Type);
    }

    [Fact]
    public async Task GetBigCategories_SuccessWithoutPayload_ReturnsProviderUnavailable()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, """{ "success": true }"""));

        var result = await provider.GetBigCategoriesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.ProviderUnavailable, result.Error.Type);
    }

    // ---------------------------------------------------------------- caching

    [Fact]
    public async Task FailedResults_AreNotCached()
    {
        var calls = 0;
        var (provider, handler) = CreateProvider(
            _ =>
            {
                calls++;
                return calls == 1
                    ? JsonResponse(HttpStatusCode.InternalServerError, ErrorJson("INTERNAL_ERROR", "upstream exploded"))
                    : JsonResponse(HttpStatusCode.OK, BigCategoriesJson);
            },
            new InMemoryCache());

        var first = await provider.GetBigCategoriesAsync();
        var second = await provider.GetBigCategoriesAsync();

        Assert.True(first.IsFailure);
        Assert.Equal(ErrorType.ProviderUnavailable, first.Error.Type);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SuccessfulResults_AreCached()
    {
        var (provider, handler) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, BigCategoriesJson), new InMemoryCache());

        var first = await provider.GetBigCategoriesAsync();
        var second = await provider.GetBigCategoriesAsync();

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Count, second.Value.Count);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetItems_UsesCachedCategoriesForImageInheritance()
    {
        var (provider, handler) = CreateProvider(RouteCatalog(), new InMemoryCache());

        await provider.GetItemsAsync();
        await provider.GetItemsAsync();

        Assert.Equal(2, handler.Requests.Count);
        Assert.Single(handler.Requests, request =>
            request.Url.Contains("/catalog/subcategories", StringComparison.Ordinal));
        Assert.Single(handler.Requests, request =>
            request.Url.Contains("/catalog/categories?for_ui=true", StringComparison.Ordinal));
        Assert.All(handler.Requests, request => Assert.Equal(AuthorizationHeader, request.Authorization));
    }

    // --------------------------------------------------------- account health

    [Fact]
    public async Task CheckHealth_ReturnsEnabledFlagAndCachesResult()
    {
        var (provider, handler) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, AccountJson), new InMemoryCache());

        var first = await provider.CheckHealthAsync();
        var second = await provider.CheckHealthAsync();

        Assert.True(first.IsSuccess);
        Assert.True(first.Value);
        Assert.True(second.IsSuccess);
        Assert.True(second.Value);
        Assert.Equal($"{BaseUrl}/account", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task CheckHealth_DisabledAccount_ReturnsFalse()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, DisabledAccountJson));

        var result = await provider.CheckHealthAsync();

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public async Task CheckHealth_UpstreamFailure_ReturnsProviderUnavailable()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(
            HttpStatusCode.Forbidden,
            ErrorJson("ACCOUNT_FROZEN", "frozen account with wallet 1234.56")));

        var result = await provider.CheckHealthAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.ProviderUnavailable, result.Error.Type);
        Assert.DoesNotContain("1234.56", result.Error.Message, StringComparison.Ordinal);
    }

    // --------------------------------------------------- requirements parsing

    [Fact]
    public async Task GetCategoryRequirements_NoRequirementsFound_ReturnsEmptySuccess()
    {
        var (provider, handler) = CreateProvider(_ => JsonResponse(
            HttpStatusCode.NotFound,
            ErrorJson("NO_REQUIREMENTS_FOUND", "no requirements for this category")));

        var result = await provider.GetCategoryRequirementsAsync("12");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        Assert.Equal($"{BaseUrl}/catalog/category/12/requirements", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task GetCategoryRequirements_MapsDataTypesOptionsAndLengths()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, RequirementsJson));

        var result = await provider.GetCategoryRequirementsAsync("12");

        Assert.True(result.IsSuccess);
        var requirements = result.Value;
        Assert.Equal(5, requirements.Count);

        var playerId = requirements[0];
        Assert.Equal("player_id", playerId.FieldName);
        Assert.Equal(RequirementDataType.Text, playerId.DataType);
        Assert.Equal("Enter player ID", playerId.Placeholder);
        Assert.Equal(5, playerId.MinLength);
        Assert.Equal(20, playerId.MaxLength);
        Assert.Empty(playerId.SelectOptions);

        var server = requirements[1];
        Assert.Equal(RequirementDataType.SingleSelect, server.DataType);
        Assert.Equal(new[] { "NA", "EU", "3" }, server.SelectOptions);

        var amount = requirements[2];
        Assert.Equal(RequirementDataType.Number, amount.DataType);
        Assert.Null(amount.Placeholder);
        Assert.Null(amount.MinLength);
        Assert.Null(amount.MaxLength);

        var regions = requirements[3];
        Assert.Equal(RequirementDataType.MultiSelect, regions.DataType);
        Assert.Equal(new[] { "na" }, regions.SelectOptions);

        var unknown = requirements[4];
        Assert.Equal(RequirementDataType.Text, unknown.DataType);
    }

    // ------------------------------------------------------ dependency wiring

    [Fact]
    public void AddInfrastructure_RegistersCatalogAndExchangeRateProvidersWithCache()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Shop2TopUp:BaseUrl"] = BaseUrl,
                ["Shop2TopUp:KeyId"] = KeyId,
                ["Shop2TopUp:KeySecret"] = KeySecret,
                ["Shop2TopUp:ProviderUid"] = ProviderUid.ToString("D"),
                ["Binance:BaseUrl"] = "https://rates.test",
                ["Cache:UseRedis"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        using var scope = serviceProvider.CreateScope();

        var catalogProviders = scope.ServiceProvider.GetServices<ICatalogProvider>().ToArray();
        var exchangeRateProviders = scope.ServiceProvider.GetServices<IExchangeRateProvider>().ToArray();

        Assert.Single(catalogProviders);
        Assert.IsType<Shop2TopUpCatalogProvider>(catalogProviders[0]);
        Assert.IsType<Shop2TopUpCatalogProvider>(scope.ServiceProvider.GetRequiredService<Shop2TopUpCatalogProvider>());
        Assert.Single(exchangeRateProviders);
        Assert.IsType<BinanceExchangeRateProvider>(exchangeRateProviders[0]);
        Assert.IsType<RedisHybridCacheService>(scope.ServiceProvider.GetRequiredService<ICacheService>());
    }

    [Fact]
    public void AddInfrastructure_ResolvesCatalogProviderWithMaximumTimeoutConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Shop2TopUp:BaseUrl"] = BaseUrl,
                ["Shop2TopUp:KeyId"] = KeyId,
                ["Shop2TopUp:KeySecret"] = KeySecret,
                ["Shop2TopUp:ProviderUid"] = ProviderUid.ToString("D"),
                ["Shop2TopUp:TimeoutSeconds"] = "60",
                ["Binance:BaseUrl"] = "https://rates.test",
                ["Binance:TimeoutSeconds"] = "30",
                ["Cache:UseRedis"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        using var scope = serviceProvider.CreateScope();

        // Resolving the typed clients builds the resilience pipelines, which validates
        // the attempt / total / circuit-breaker timeout relationship.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<Shop2TopUpCatalogProvider>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<BinanceExchangeRateProvider>());
    }

    // ------------------------------------------------------------- fixtures

    private const string BigCategoriesJson = """
    {
      "success": true,
      "big_categories": [
        { "id": 1, "name": "Game Cards", "description": "Digital game cards", "image": "https://cdn.test/big.png" },
        { "id": "2", "name": "Top Ups", "description": "Game top ups", "image": "javascript:alert(1)" }
      ]
    }
    """;

    private const string CategoriesJson = """
    {
      "success": true,
      "categories": [
        { "id": 12, "name": "PlayStation Store Gift Card USA", "description": "US region", "big_category_id": 1, "big_category_name": "Game Cards", "image": "https://cdn.test/ps-usa.png" },
        { "id": "13", "name": "Steam Wallet Global", "description": "Global top ups", "big_category_id": "2", "big_category_name": "Top Ups", "image": "http://cdn.test/steam.png" }
      ]
    }
    """;

    private const string SubcategoriesJson = """
    {
      "success": true,
      "subcategories": [
        { "item_id": 999, "name": "PlayStation 10 USD", "description": "PSN 10", "category_id": 12, "category_name": "PlayStation Store Gift Card USA", "price": "0.95", "prices_locked": false, "in_stock": true, "product_type": "topup" },
        { "item_id": "1000", "name": "PlayStation 25 USD", "description": "PSN 25", "category_id": "12", "category_name": "PlayStation Store Gift Card USA", "prices_locked": true, "in_stock": false, "product_type": "voucher" },
        { "item_id": 1001, "name": "Steam 5 USD", "description": "Steam wallet", "category_id": 13, "category_name": "Steam Wallet Global", "price": "4.00", "in_stock": true, "product_type": "voucher", "image": "https://cdn.test/steam-5.png" }
      ]
    }
    """;

    private const string UnsafeImageSubcategoriesJson = """
    {
      "success": true,
      "subcategories": [
        { "item_id": 1, "name": "Unsafe own image", "description": "", "category_id": 12, "category_name": "PlayStation Store Gift Card USA", "price": "1.00", "in_stock": true, "product_type": "topup", "image": "javascript:alert(1)" },
        { "item_id": 2, "name": "Http own image", "description": "", "category_id": 13, "category_name": "Steam Wallet Global", "price": "1.00", "in_stock": true, "product_type": "topup", "image": "http://cdn.test/x.png" },
        { "item_id": 3, "name": "No image at all", "description": "", "category_id": 14, "category_name": "Unknown Region", "price": "1.00", "in_stock": true, "product_type": "topup" }
      ]
    }
    """;

    private const string UnsafeImageCategoriesJson = """
    {
      "success": true,
      "categories": [
        { "id": 12, "name": "PlayStation Store Gift Card USA", "description": "", "big_category_id": 1, "big_category_name": "Game Cards", "image": "https://cdn.test/ps-usa.png" },
        { "id": 13, "name": "Steam Wallet Global", "description": "", "big_category_id": 2, "big_category_name": "Top Ups", "image": "javascript:alert(2)" },
        { "id": 14, "name": "Unknown Region", "description": "", "big_category_id": 2, "big_category_name": "Top Ups" }
      ]
    }
    """;

    private const string LockedPriceSubcategoriesJson = """
    {
      "success": true,
      "subcategories": [
        { "item_id": 1, "name": "Locked without price", "description": "", "category_id": 12, "category_name": "PlayStation Store Gift Card USA", "prices_locked": true, "in_stock": true, "product_type": "topup" },
        { "item_id": 2, "name": "Locked with price", "description": "", "category_id": 12, "category_name": "PlayStation Store Gift Card USA", "price": "0.99", "prices_locked": true, "in_stock": true, "product_type": "topup" },
        { "item_id": 3, "name": "Unparseable price", "description": "", "category_id": 12, "category_name": "PlayStation Store Gift Card USA", "price": "not-a-number", "in_stock": true, "product_type": "topup" },
        { "item_id": 4, "name": "Unknown product type", "description": "", "category_id": 12, "category_name": "PlayStation Store Gift Card USA", "price": "1.00", "in_stock": true, "product_type": "mystery" }
      ]
    }
    """;

    private const string PriceJson = """
    {
      "success": true,
      "price": {
        "item_id": 999,
        "item_name": "PlayStation 10 USD",
        "unit_price": "0.950000",
        "currency": "USD",
        "original_price": "0.990000",
        "discount_applied": true,
        "in_stock": true,
        "timestamp": "2026-11-25T10:00:00Z"
      }
    }
    """;

    private const string PriceWithInvalidUnitPriceJson = """
    {
      "success": true,
      "price": {
        "item_id": 999,
        "item_name": "PlayStation 10 USD",
        "unit_price": "n/a",
        "currency": "USD",
        "in_stock": true
      }
    }
    """;

    private const string RequirementsJson = """
    {
      "success": true,
      "requirements": [
        { "field_name": "player_id", "data_type": "text", "placeholder": "Enter player ID", "min_length": 5, "max_length": "20" },
        { "field_name": "server", "data_type": "single_select", "select_options": ["NA", "EU", 3] },
        { "field_name": "amount", "data_type": "number" },
        { "field_name": "regions", "data_type": "multi_select", "select_options": [{ "value": "na", "label": "North America" }] },
        { "field_name": "legacy_field", "data_type": "unknown_type" }
      ]
    }
    """;

    private const string AccountJson = """
    {
      "success": true,
      "account": {
        "enabled": true,
        "email": "reseller@example.com",
        "wallet_balance": "1234.56",
        "username": "reseller"
      }
    }
    """;

    private const string DisabledAccountJson = """
    {
      "success": true,
      "account": { "enabled": false, "email": "reseller@example.com", "wallet_balance": "0.00" }
    }
    """;

    // -------------------------------------------------------------- helpers

    private static Func<HttpRequestMessage, HttpResponseMessage> RouteCatalog() =>
        request =>
        {
            var pathAndQuery = request.RequestUri!.PathAndQuery;
            if (pathAndQuery.StartsWith(BasePath + "/catalog/subcategories", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, SubcategoriesJson);
            }

            if (pathAndQuery.StartsWith(BasePath + "/catalog/categories", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, CategoriesJson);
            }

            return JsonResponse(HttpStatusCode.NotFound, ErrorJson("ITEM_NOT_FOUND", "unmapped path"));
        };

    private static (Shop2TopUpCatalogProvider Provider, RecordingHandler Handler) CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        ICacheService? cache = null,
        CacheOptions? cacheOptions = null)
    {
        var handler = new RecordingHandler(responder);
        var httpClient = new HttpClient(handler, disposeHandler: false);

        var provider = new Shop2TopUpCatalogProvider(
            httpClient,
            Options.Create(new Shop2TopUpOptions
            {
                BaseUrl = BaseUrl,
                KeyId = KeyId,
                KeySecret = KeySecret,
                ProviderUid = ProviderUid,
            }),
            Options.Create(cacheOptions ?? new CacheOptions()),
            new PricingService(Options.Create(new PricingOptions
            {
                DefaultMarkupPercent = DefaultMarkupPercent,
                MinimumMarginUsd = MinimumMarginUsd,
            })),
            new RegionResolver(),
            cache ?? new PassThroughCache(),
            NullLogger<Shop2TopUpCatalogProvider>.Instance);

        return (provider, handler);
    }

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string json,
        int? retryAfterSeconds = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        if (retryAfterSeconds is { } seconds)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        }

        return response;
    }

    private static string ErrorJson(string code, string message, int? retryAfter = null)
    {
        var retryAfterJson = retryAfter is { } seconds
            ? $", \"retry_after\": {seconds.ToString(CultureInfo.InvariantCulture)}"
            : string.Empty;

        return $$"""
        {
          "success": false,
          "error": {
            "code": "{{code}}",
            "message": "{{message}}",
            "retryable": false{{retryAfterJson}}
          }
        }
        """;
    }

    private sealed record RecordedRequest(HttpMethod Method, string Url, string? Authorization);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var authorization = request.Headers.TryGetValues("Authorization", out var values)
                ? string.Join(" ", values)
                : null;

            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.AbsoluteUri ?? string.Empty,
                authorization));

            return Task.FromResult(responder(request));
        }
    }

    private sealed class PassThroughCache : ICacheService
    {
        public Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<T>> factory,
            TimeSpan ttl,
            CancellationToken cancellationToken = default) =>
            factory(cancellationToken).AsTask();
    }

    private sealed class InMemoryCache : ICacheService
    {
        private readonly Dictionary<string, object> _entries = new(StringComparer.Ordinal);

        public async Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<T>> factory,
            TimeSpan ttl,
            CancellationToken cancellationToken = default)
        {
            if (_entries.TryGetValue(key, out var cached) && cached is T typed)
            {
                return typed;
            }

            var value = await factory(cancellationToken);
            _entries[key] = value!;
            return value;
        }
    }
}

public sealed class UrlSanitizerTests
{
    [Theory]
    [InlineData("https://cdn.example.com/image.png", "https://cdn.example.com/image.png")]
    [InlineData("  https://cdn.example.com/image.png  ", "https://cdn.example.com/image.png")]
    [InlineData("HTTPS://CDN.EXAMPLE.COM/Image.png", "https://cdn.example.com/Image.png")]
    public void SanitizeHttpsUrl_AcceptsAbsoluteHttpsUrls(string raw, string expected)
    {
        Assert.Equal(expected, UrlSanitizer.SanitizeHttpsUrl(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://cdn.example.com/image.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("file:///etc/passwd")]
    [InlineData("//cdn.example.com/image.png")]
    [InlineData("/relative/image.png")]
    [InlineData("not a url")]
    [InlineData("https://")]
    public void SanitizeHttpsUrl_RejectsAnythingThatIsNotAbsoluteHttps(string? raw)
    {
        Assert.Null(UrlSanitizer.SanitizeHttpsUrl(raw));
    }
}

public sealed class Shop2TopUpOptionsTests
{
    [Fact]
    public void Defaults_AreSafeAndMatchContract()
    {
        var options = new Shop2TopUpOptions();

        Assert.Equal("Shop2TopUp", Shop2TopUpOptions.SectionName);
        Assert.True(Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri));
        Assert.NotNull(baseUri);
        Assert.Equal(Uri.UriSchemeHttps, baseUri.Scheme);
        Assert.EndsWith("/api/endpoints/v1", options.BaseUrl, StringComparison.Ordinal);
        Assert.Equal(Guid.Parse("d4f8a2c1-7b3e-4f90-9a12-6e8b5c3d1f42"), options.ProviderUid);
        Assert.Equal(string.Empty, options.KeyId);
        Assert.Equal(string.Empty, options.KeySecret);
        Assert.Equal(8, options.TimeoutSeconds);
    }

    [Fact]
    public void Validation_RejectsEmptyProviderUid()
    {
        var options = new Shop2TopUpOptions
        {
            KeyId = "id",
            KeySecret = "secret",
            ProviderUid = Guid.Empty,
        };

        var results = ValidateObject(options);

        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(Shop2TopUpOptions.ProviderUid), StringComparer.Ordinal));
    }

    [Fact]
    public void Validation_RequiresBothCredentialParts()
    {
        var options = new Shop2TopUpOptions();

        var results = ValidateObject(options);

        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(Shop2TopUpOptions.KeyId), StringComparer.Ordinal));
        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(Shop2TopUpOptions.KeySecret), StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void Validation_RejectsTimeoutOutsideRange(int timeoutSeconds)
    {
        var options = new Shop2TopUpOptions
        {
            KeyId = "id",
            KeySecret = "secret",
            TimeoutSeconds = timeoutSeconds,
        };

        var results = ValidateObject(options);

        Assert.NotEmpty(results);
    }

    internal static List<ValidationResult> ValidateObject(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }
}

public sealed class CacheOptionsTests
{
    [Fact]
    public void Defaults_MatchContract()
    {
        var options = new CacheOptions();

        Assert.Equal("Cache", CacheOptions.SectionName);
        Assert.Equal(string.Empty, options.RedisConnectionString);
        Assert.True(options.UseRedis);
        Assert.Equal("verkkugames:v1:", options.KeyPrefix);
        Assert.Equal(1800, options.BigCategoriesSeconds);
        Assert.Equal(900, options.CategoriesSeconds);
        Assert.Equal(600, options.ItemsSeconds);
        Assert.Equal(1800, options.RequirementsSeconds);
        Assert.Equal(60, options.ItemPriceSeconds);
        Assert.Equal(60, options.ExchangeRatesSeconds);
        Assert.Equal(300, options.AccountHealthSeconds);
    }

    [Fact]
    public void Validation_RequiresRedisConnectionStringWhenRedisEnabled()
    {
        var options = new CacheOptions
        {
            UseRedis = true,
            RedisConnectionString = "  ",
        };

        var results = Shop2TopUpOptionsTests.ValidateObject(options);

        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(CacheOptions.RedisConnectionString), StringComparer.Ordinal));
    }

    [Fact]
    public void Validation_DoesNotRequireRedisConnectionStringWhenRedisDisabled()
    {
        var options = new CacheOptions
        {
            UseRedis = false,
            RedisConnectionString = string.Empty,
        };

        Assert.Empty(Shop2TopUpOptionsTests.ValidateObject(options));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(86401)]
    public void Validation_RejectsTtlOutsideRange(int seconds)
    {
        var options = new CacheOptions
        {
            UseRedis = false,
            ItemsSeconds = seconds,
        };

        Assert.NotEmpty(Shop2TopUpOptionsTests.ValidateObject(options));
    }
}
