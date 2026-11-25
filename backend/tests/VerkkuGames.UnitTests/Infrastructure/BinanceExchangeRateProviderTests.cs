using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Domain.Common;
using VerkkuGames.Domain.Pricing;
using VerkkuGames.Domain.ValueObjects;
using VerkkuGames.Infrastructure.Options;
using VerkkuGames.Infrastructure.Providers.Binance;
using Xunit;

namespace VerkkuGames.UnitTests.Infrastructure;

public sealed class BinanceExchangeRateProviderTests
{
    private const string BaseUrl = "https://rates.test";

    private const string ExpectedTickerPathAndQuery =
        "/api/v3/ticker/price?symbols=%5B%22BTCUSDT%22,%22ETHUSDT%22,%22USDCUSDT%22%5D";

    private const string PricesJson = """
    [
      { "symbol": "BTCUSDT", "price": "96420.15000000" },
      { "symbol": "ETHUSDT", "price": "3200.50000000" },
      { "symbol": "USDCUSDT", "price": "1.00010000" }
    ]
    """;

    [Fact]
    public async Task GetCryptoRates_SendsExactTickerEndpoint_WithoutAuthorizationHeader()
    {
        var (provider, handler) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, PricesJson));

        var result = await provider.GetCryptoRatesAsync();

        Assert.True(result.IsSuccess);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"{BaseUrl}{ExpectedTickerPathAndQuery}", request.Url);
        Assert.Null(request.Authorization);
    }

    [Fact]
    public async Task GetCryptoRates_UnderSpanishCulture_ParsesInvariantDecimalsAndComputesRates()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-VE");
            CultureInfo.CurrentUICulture = new CultureInfo("es-VE");

            var (provider, _) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, PricesJson));
            var before = DateTimeOffset.UtcNow;

            var result = await provider.GetCryptoRatesAsync();

            Assert.True(result.IsSuccess);
            var snapshot = result.Value;
            Assert.Equal(CurrencyCode.USD, snapshot.BaseCurrency);
            Assert.Equal(5, snapshot.RatesPerUsd.Count);
            Assert.Equal(1m, snapshot.RatesPerUsd[CurrencyCode.USD]);
            Assert.Equal(1m, snapshot.RatesPerUsd[CurrencyCode.USDT]);
            Assert.Equal(1m / 1.00010000m, snapshot.RatesPerUsd[CurrencyCode.USDC]);
            Assert.Equal(1m / 96420.15000000m, snapshot.RatesPerUsd[CurrencyCode.BTC]);
            Assert.Equal(1m / 3200.50000000m, snapshot.RatesPerUsd[CurrencyCode.ETH]);
            Assert.InRange(snapshot.RetrievedAtUtc, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public async Task GetCryptoRates_IgnoresUnknownSymbols()
    {
        var json = TickerJson(
            ("BTCUSDT", "96420.15"),
            ("ETHUSDT", "3200.5"),
            ("USDCUSDT", "1.0001"),
            ("DOGEUSDT", "0.42"));

        var (provider, _) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, json));

        var result = await provider.GetCryptoRatesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.RatesPerUsd.Count);
        Assert.Equal(1m / 96420.15m, result.Value.RatesPerUsd[CurrencyCode.BTC]);
    }

    [Theory]
    [InlineData("BTCUSDT")]
    [InlineData("ETHUSDT")]
    [InlineData("USDCUSDT")]
    public async Task GetCryptoRates_MissingRequiredSymbol_ReturnsProviderUnavailable(string missingSymbol)
    {
        var tickers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BTCUSDT"] = "96420.15000000",
            ["ETHUSDT"] = "3200.50000000",
            ["USDCUSDT"] = "1.00010000",
        };
        tickers.Remove(missingSymbol);

        var json = TickerJson(tickers.Select(pair => (pair.Key, pair.Value)).ToArray());
        var (provider, _) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, json));

        var result = await provider.GetCryptoRatesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.ProviderUnavailable, result.Error.Type);
        Assert.Equal("exchange_rates_unavailable", result.Error.Code);
        Assert.Equal("Exchange rates are temporarily unavailable.", result.Error.Message);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-1.5")]
    [InlineData("")]
    public async Task GetCryptoRates_InvalidPrice_ReturnsProviderUnavailable(string invalidPrice)
    {
        var json = TickerJson(
            ("BTCUSDT", "96420.15000000"),
            ("ETHUSDT", invalidPrice),
            ("USDCUSDT", "1.00010000"));

        var (provider, _) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, json));

        var result = await provider.GetCryptoRatesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.ProviderUnavailable, result.Error.Type);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public async Task GetCryptoRates_Http500_ReturnsProviderUnavailableWithoutFallbackRates()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(
            HttpStatusCode.InternalServerError,
            """{ "code": -1000, "msg": "An unknown error occurred while processing the request." }"""));

        var result = await provider.GetCryptoRatesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.ProviderUnavailable, result.Error.Type);
        Assert.Equal("exchange_rates_unavailable", result.Error.Code);
        Assert.DoesNotContain("unknown error", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public async Task GetCryptoRates_MalformedJson_ReturnsProviderUnavailable()
    {
        var (provider, _) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, "{ not json "));

        var result = await provider.GetCryptoRatesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.ProviderUnavailable, result.Error.Type);
    }

    [Fact]
    public async Task GetCryptoRates_NetworkFailure_ReturnsProviderUnavailable()
    {
        var (provider, _) = CreateProvider(_ => throw new HttpRequestException("name resolution failed"));

        var result = await provider.GetCryptoRatesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.ProviderUnavailable, result.Error.Type);
    }

    [Fact]
    public async Task GetCryptoRates_Timeout_ReturnsProviderUnavailable()
    {
        var (provider, _) = CreateProvider(_ => throw new TimeoutRejectedException("attempt timed out"));

        var result = await provider.GetCryptoRatesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.ProviderUnavailable, result.Error.Type);
    }

    [Fact]
    public async Task GetCryptoRates_CachesSuccessfulSnapshot()
    {
        var (provider, handler) = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, PricesJson), new InMemoryCache());

        var first = await provider.GetCryptoRatesAsync();
        var second = await provider.GetCryptoRatesAsync();

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.RatesPerUsd[CurrencyCode.BTC], second.Value.RatesPerUsd[CurrencyCode.BTC]);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetCryptoRates_DoesNotCacheFailures()
    {
        var calls = 0;
        var (provider, handler) = CreateProvider(
            _ =>
            {
                calls++;
                return calls == 1
                    ? JsonResponse(HttpStatusCode.InternalServerError, """{ "code": -1000, "msg": "boom" }""")
                    : JsonResponse(HttpStatusCode.OK, PricesJson);
            },
            new InMemoryCache());

        var first = await provider.GetCryptoRatesAsync();
        var second = await provider.GetCryptoRatesAsync();

        Assert.True(first.IsFailure);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
    }

    // ------------------------------------------------------------- helpers

    private static (BinanceExchangeRateProvider Provider, RecordingHandler Handler) CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        ICacheService? cache = null,
        CacheOptions? cacheOptions = null)
    {
        var handler = new RecordingHandler(responder);
        var httpClient = new HttpClient(handler, disposeHandler: false);

        var provider = new BinanceExchangeRateProvider(
            httpClient,
            Options.Create(new BinanceOptions { BaseUrl = BaseUrl }),
            Options.Create(cacheOptions ?? new CacheOptions()),
            cache ?? new PassThroughCache(),
            NullLogger<BinanceExchangeRateProvider>.Instance);

        return (provider, handler);
    }

    private static string TickerJson(params (string Symbol, string Price)[] tickers) =>
        "[" + string.Join(
            ",",
            tickers.Select(ticker =>
                $"{{\"symbol\":\"{ticker.Symbol}\",\"price\":\"{ticker.Price}\"}}")) + "]";

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

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

public sealed class BinanceOptionsTests
{
    [Fact]
    public void Defaults_MatchContract()
    {
        var options = new BinanceOptions();

        Assert.Equal("Binance", BinanceOptions.SectionName);
        Assert.Equal("https://data-api.binance.vision", options.BaseUrl);
        Assert.Equal(5, options.TimeoutSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void Validation_RejectsTimeoutOutsideRange(int timeoutSeconds)
    {
        var options = new BinanceOptions { TimeoutSeconds = timeoutSeconds };

        Assert.NotEmpty(Shop2TopUpOptionsTests.ValidateObject(options));
    }
}
