using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Application.Options;
using VerkkuGames.Application.Services;
using VerkkuGames.Domain.Catalog;
using VerkkuGames.Domain.Common;
using VerkkuGames.Domain.ValueObjects;
using VerkkuGames.Infrastructure.Caching;
using VerkkuGames.Infrastructure.Options;
using VerkkuGames.Infrastructure.Providers.FazerCards;
using Xunit;

namespace VerkkuGames.UnitTests.Infrastructure;

public sealed class FazerCardsCatalogProviderTests
{
    private const string BaseUrl = "https://fazer.test/api/v2";
    private const string ApiKey = "test-fazer-key";
    private static readonly Guid ProviderUid = Guid.Parse("9075f324-4177-45e9-a79c-f23722258b8f");

    [Fact]
    public async Task GetBigCategories_SendsApiKeyHeaderAndMapsGames()
    {
        var (provider, handler) = CreateProvider(_ => Json(HttpStatusCode.OK, GameCatalogJson));

        var result = await provider.GetBigCategoriesAsync();

        Assert.True(result.IsSuccess);
        var request = Assert.Single(handler.Requests);
        Assert.Equal($"{BaseUrl}/gamekeys?include_ui=true&limit=50", request.Url);
        Assert.Equal(ApiKey, request.ApiKey);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("7_days_to_die_global", result.Value[0].ExternalId);
        Assert.Equal("7 Days to Die", result.Value[0].Name);
        Assert.Equal(ProviderUid, result.Value[0].ProviderUid);
        // Steam appid fallback when no provider cover is supplied.
        Assert.Equal(
            "https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/251570/header.jpg",
            result.Value[0].ImageUrl);
        // Provider cover takes precedence.
        Assert.Equal(
            "https://reseller.fazercards.com/api/v2/media/game-key-covers/abc.jpg",
            result.Value[1].ImageUrl);
    }

    [Fact]
    public async Task GetItems_ParsesGameNameAndSnakeCaseIds_AppliesMarkupNeverLeaksWholesale()
    {
        var (provider, _) = CreateProvider(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/gamekeys/keys", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, KeysJson)
                : Json(HttpStatusCode.OK, GameCatalogJson));

        var result = await provider.GetItemsAsync("7_days_to_die_global");

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value);
        Assert.Equal("7_days_to_die_global--7_days_to_die", item.ExternalId);
        Assert.Equal("7 Days to Die", item.CategoryName);
        Assert.Equal("Full game key", item.Description);
        Assert.Equal(GameKeyVariant.BaseGame, item.KeyVariant);
        Assert.Equal("Steam", item.Platform);
        Assert.Equal(RegionCode.GLOBAL, item.Region);
        Assert.True(item.InStock);
        Assert.False(item.PricesLocked);
        Assert.Equal(17.65m, item.RetailPriceUsd!.Value.Amount);
        Assert.NotEqual(16.0491m, item.RetailPriceUsd.Value.Amount);
    }

    [Fact]
    public async Task GetItemById_ResolvesCompositeIdDirectlyWithoutFullScan()
    {
        var (provider, handler) = CreateProvider(_ => Json(HttpStatusCode.OK, KeysJson));

        var result = await provider.GetItemByIdAsync("7_days_to_die_global--7_days_to_die");

        Assert.True(result.IsSuccess);
        Assert.Equal("7 Days to Die", result.Value.Name);
        Assert.Single(handler.Requests);
        Assert.Contains("game_id=7_days_to_die_global", handler.Requests[0].Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetItems_FollowsCursorAndStops()
    {
        var (provider, handler) = CreateProvider(request =>
        {
            var query = request.RequestUri!.Query;
            return query.Contains("cursor=opaque", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, GameCatalogPage2Json)
                : Json(HttpStatusCode.OK, GameCatalogPage1Json);
        });

        var result = await provider.GetBigCategoriesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(2, result.Value.Count);
    }

    [Fact]
    public async Task GetCategoryRequirements_WithNoRestriction_ReturnsEmpty()
    {
        var (provider, handler) = CreateProvider(_ => Json(
            HttpStatusCode.OK,
            """{"ok":true,"region_type":"GLOBAL","has_availability":false,"available":[],"unavailable":[]}"""));

        var result = await provider.GetCategoryRequirementsAsync("7_days_to_die_global");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CheckHealth_UsesMeWithoutLeakingEmail()
    {
        var (provider, handler) = CreateProvider(_ => Json(
            HttpStatusCode.OK,
            """{"ok":true,"login":"partner","email":"secret@example.com"}"""));

        var result = await provider.CheckHealthAsync();

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.Equal($"{BaseUrl}/me", Assert.Single(handler.Requests).Url);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task SensitiveErrors_ReturnSanitizedProviderUnavailable(HttpStatusCode status)
    {
        var (provider, _) = CreateProvider(_ => Json(
            status,
            """{"ok":false,"error":"partner secret@example.com key abc","code":"FORBIDDEN"}"""));

        var result = await provider.GetBigCategoriesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.ProviderUnavailable, result.Error.Type);
        Assert.DoesNotContain("fazercards", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret@example.com", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ApiKey, result.Error.Message, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------- fixtures

    private const string GameCatalogJson = """
    {
      "ok": true,
      "kind": "game_key",
      "items": [
        { "name": "7 Days to Die", "game_id": "7_days_to_die_global", "region": "GLOBAL", "platform": "Steam", "region_restriction": false, "appid": 251570 },
        { "name": "Abyssus", "game_id": "abyssus_global", "region": "GLOBAL", "platform": "Steam", "region_restriction": false, "imageurl": "https://reseller.fazercards.com/api/v2/media/game-key-covers/abc.jpg" }
      ],
      "meta": { "total": 2, "limit": 50, "next_cursor": null, "has_more": false }
    }
    """;

    private const string GameCatalogPage1Json = """
    {
      "ok": true,
      "kind": "game_key",
      "items": [
        { "name": "7 Days to Die", "game_id": "7_days_to_die_global", "region": "GLOBAL", "platform": "Steam", "region_restriction": false }
      ],
      "meta": { "total": 2, "limit": 50, "next_cursor": "opaque", "has_more": true }
    }
    """;

    private const string GameCatalogPage2Json = """
    {
      "ok": true,
      "kind": "game_key",
      "items": [
        { "name": "Abyssus", "game_id": "abyssus_global", "region": "GLOBAL", "platform": "Steam", "region_restriction": false }
      ],
      "meta": { "total": 2, "limit": 50, "next_cursor": null, "has_more": false }
    }
    """;

    private const string KeysJson = """
    {
      "ok": true,
      "kind": "game_key",
      "game_id": "7_days_to_die_global",
      "GameName": "7 Days to Die",
      "region": "GLOBAL",
      "platform": "Steam",
      "region_restriction": false,
      "keys": [
        { "key_id": "7_days_to_die", "name": "7 Days to Die", "price_usd": "16.0491", "stock": 85 }
      ]
    }
    """;

    private static (FazerCardsCatalogProvider Provider, RecordingHandler Handler) CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new RecordingHandler(responder);
        var httpClient = new HttpClient(handler, disposeHandler: false);
        var provider = new FazerCardsCatalogProvider(
            httpClient,
            Options.Create(new FazerCardsOptions { BaseUrl = BaseUrl, ApiKey = ApiKey, ProviderUid = ProviderUid }),
            Options.Create(new CacheOptions()),
            new PricingService(Options.Create(new PricingOptions { DefaultMarkupPercent = 10m, MinimumMarginUsd = 0.25m })),
            new RegionResolver(),
            new PassThroughCache(),
            NullLogger<FazerCardsCatalogProvider>.Instance);
        return (provider, handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed record RecordedRequest(HttpMethod Method, string Url, string? ApiKey);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var apiKey = request.Headers.TryGetValues("X-API-Key", out var values)
                ? string.Join(" ", values)
                : null;
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri?.AbsoluteUri ?? string.Empty, apiKey));
            return Task.FromResult(responder(request));
        }
    }

    private sealed class PassThroughCache : ICacheService
    {
        public Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<T>> factory,
            TimeSpan ttl,
            CancellationToken cancellationToken = default) => factory(cancellationToken).AsTask();
    }
}
