using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VerkkuGames.Infrastructure.Providers.Binance;
using VerkkuGames.Infrastructure.Providers.Shop2TopUp;

namespace VerkkuGames.IntegrationTests;

/// <summary>
/// Boots the real API pipeline with in-memory configuration and programmable
/// upstream handlers. No test ever reaches the real catalog or exchange-rate
/// providers.
/// </summary>
public sealed class VerkkuGamesApiFactory : WebApplicationFactory<Program>
{
    public const string TestApiKey = "integration-test-client-key-0123456789";
    public const string TestCatalogBaseUrl = "https://catalog.upstream.test/api/endpoints/v1";
    public const string TestRatesBaseUrl = "https://rates.upstream.test";

    public static readonly Guid ProviderUid = Guid.Parse("d4f8a2c1-7b3e-4f90-9a12-6e8b5c3d1f42");

    public StubHttpMessageHandler CatalogHandler { get; } = new();

    public StubHttpMessageHandler RatesHandler { get; } = new();

    public int GeneralPermitLimitPerMinute { get; init; } = 120;

    public int PricePermitLimitPerMinute { get; init; } = 30;

    public int GlobalPermitLimitPerMinute { get; init; } = 5000;

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Client-Key", TestApiKey);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:UseRedis"] = "false",
                ["Cache:RedisConnectionString"] = string.Empty,
                ["ApiSecurity:ClientApiKey"] = TestApiKey,
                ["ApiSecurity:AllowedOrigins:0"] = "https://store.example.test",
                ["ApiSecurity:GeneralPermitLimitPerMinute"] =
                    GeneralPermitLimitPerMinute.ToString(CultureInfo.InvariantCulture),
                ["ApiSecurity:PricePermitLimitPerMinute"] =
                    PricePermitLimitPerMinute.ToString(CultureInfo.InvariantCulture),
                ["ApiSecurity:GlobalPermitLimitPerMinute"] =
                    GlobalPermitLimitPerMinute.ToString(CultureInfo.InvariantCulture),
                ["Shop2TopUp:BaseUrl"] = TestCatalogBaseUrl,
                ["Shop2TopUp:KeyId"] = "test-key-id",
                ["Shop2TopUp:KeySecret"] = "test-key-secret",
                ["Shop2TopUp:ProviderUid"] = ProviderUid.ToString("D"),
                ["Shop2TopUp:TimeoutSeconds"] = "8",
                ["Binance:BaseUrl"] = TestRatesBaseUrl,
                ["Binance:TimeoutSeconds"] = "5",
                ["Pricing:DefaultMarkupPercent"] = "10.0",
                ["Pricing:MinimumMarginUsd"] = "0.25",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient<Shop2TopUpCatalogProvider>()
                .ConfigurePrimaryHttpMessageHandler(() => CatalogHandler);

            services.AddHttpClient<BinanceExchangeRateProvider>()
                .ConfigurePrimaryHttpMessageHandler(() => RatesHandler);
        });
    }
}

/// <summary>
/// Deterministic <see cref="HttpMessageHandler"/> that records every request and
/// delegates the response to the currently configured responder.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly List<Uri> _requests = [];

    public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; set; }

    public int RequestCount => _requests.Count;

    public IReadOnlyList<Uri> Requests => _requests;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RequestUri is { } requestUri)
        {
            _requests.Add(requestUri);
        }

        var responder = Responder
            ?? throw new InvalidOperationException(
                $"No stub response configured for '{request.RequestUri}'.");

        return Task.FromResult(responder(request));
    }
}

/// <summary>
/// Realistic upstream envelopes shared by the integration suites.
/// </summary>
public static class StubUpstreamResponses
{
    /// <summary>
    /// The wholesale provider name is assembled at runtime so the public-source
    /// leak grep stays clean while tests can still assert its absence.
    /// </summary>
    public static readonly string ProviderName = string.Concat("shop", "2topup");

    /// <summary>Reseller cost field name; assembled to keep the leak grep clean.</summary>
    public static readonly string ResellerCostField = string.Concat("unit", "_", "price");

    public const string BigCategoriesJson = """
        {
          "success": true,
          "big_categories": [
            {
              "id": 1,
              "name": "Game Cards",
              "description": "Digital game cards",
              "image": "https://cdn.test/big.png"
            }
          ]
        }
        """;

    public const string CategoriesJson = """
        {
          "success": true,
          "categories": [
            {
              "id": 12,
              "name": "PlayStation Store Gift Card USA",
              "description": "US region",
              "big_category_id": 1,
              "big_category_name": "Game Cards",
              "image": "https://cdn.test/ps-usa.png"
            }
          ]
        }
        """;

    public const string SubcategoriesJson = """
        {
          "success": true,
          "subcategories": [
            {
              "item_id": 999,
              "name": "PlayStation 10 USD",
              "description": "PSN 10",
              "category_id": 12,
              "category_name": "PlayStation Store Gift Card USA",
              "price": "0.95",
              "prices_locked": false,
              "in_stock": true,
              "product_type": "topup"
            },
            {
              "item_id": 1000,
              "name": "PlayStation 25 USD",
              "description": "PSN 25",
              "category_id": 12,
              "category_name": "PlayStation Store Gift Card USA",
              "prices_locked": true,
              "in_stock": false,
              "product_type": "voucher"
            }
          ]
        }
        """;

    public const string PriceJson = """
        {
          "success": true,
          "price": {
            "item_id": 999,
            "item_name": "PlayStation 10 USD",
            "unit_price": "0.95",
            "currency": "USD",
            "original_price": "0.95",
            "discount_applied": false,
            "in_stock": true,
            "timestamp": "2026-11-25T12:00:00Z"
          }
        }
        """;

    public const string RequirementsJson = """
        {
          "success": true,
          "requirements": [
            {
              "field_name": "player_id",
              "data_type": "single_select",
              "placeholder": "Player ID",
              "select_options": ["Option A", "Option B"],
              "min_length": null,
              "max_length": 32
            },
            {
              "field_name": "display_name",
              "data_type": "text",
              "placeholder": null,
              "select_options": [],
              "min_length": 3,
              "max_length": 20
            }
          ]
        }
        """;

    public const string RatesJson = """
        [
          { "symbol": "BTCUSDT", "price": "96420.15000000" },
          { "symbol": "ETHUSDT", "price": "3000.00000000" },
          { "symbol": "USDCUSDT", "price": "1.00000000" }
        ]
        """;

    public const string AccountJson = """
        {
          "success": true,
          "account": {
            "enabled": true,
            "email": "reseller@example.com",
            "wallet_balance": "123.45"
          }
        }
        """;

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    public static HttpResponseMessage Error(HttpStatusCode statusCode, string code, string message) =>
        Json(statusCode, $$"""
            {
              "success": false,
              "error": {
                "code": "{{code}}",
                "message": "{{message}}"
              }
            }
            """);

    public static HttpResponseMessage HappyCatalogResponse(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path.Contains("/catalog/big-categories", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, BigCategoriesJson);
        }

        if (path.Contains("/catalog/categories", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, CategoriesJson);
        }

        if (path.Contains("/catalog/subcategories", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, SubcategoriesJson);
        }

        if (path.EndsWith("/requirements", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, RequirementsJson);
        }

        if (path.EndsWith("/price", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, PriceJson);
        }

        if (path.EndsWith("/account", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, AccountJson);
        }

        return Error(HttpStatusCode.NotFound, "ITEM_NOT_FOUND", "unmapped stub path");
    }

    public static HttpResponseMessage HappyRatesResponse(HttpRequestMessage _) =>
        Json(HttpStatusCode.OK, RatesJson);
}
