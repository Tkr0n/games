using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Domain.Common;
using VerkkuGames.Domain.Pricing;
using VerkkuGames.Domain.ValueObjects;
using VerkkuGames.Infrastructure.Options;

namespace VerkkuGames.Infrastructure.Providers.Binance;

public sealed class BinanceExchangeRateProvider : IExchangeRateProvider
{
    private const string TickerPath =
        "api/v3/ticker/price?symbols=%5B%22BTCUSDT%22,%22ETHUSDT%22,%22USDCUSDT%22%5D";

    private const string BitcoinSymbol = "BTCUSDT";
    private const string EthereumSymbol = "ETHUSDT";
    private const string UsdCoinSymbol = "USDCUSDT";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly CacheOptions _cacheOptions;
    private readonly ICacheService _cache;
    private readonly ILogger<BinanceExchangeRateProvider> _logger;
    private readonly Uri _baseAddress;

    public BinanceExchangeRateProvider(
        HttpClient httpClient,
        IOptions<BinanceOptions> options,
        IOptions<CacheOptions> cacheOptions,
        ICacheService cache,
        ILogger<BinanceExchangeRateProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cacheOptions);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _cacheOptions = cacheOptions.Value;
        _cache = cache;
        _logger = logger;
        _baseAddress = new Uri(string.Concat(options.Value.BaseUrl.TrimEnd('/'), "/"), UriKind.Absolute);
    }

    public async Task<Result<ExchangeRatesSnapshot>> GetCryptoRatesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await _cache.GetOrCreateAsync(
                "rates:binance",
                async token =>
                {
                    var result = await FetchSnapshotAsync(token).ConfigureAwait(false);
                    if (result.IsFailure)
                    {
                        // Failures must never be pinned in the cache: abort the cache factory instead.
                        throw new CachedFailureException(result.Error);
                    }

                    return result.Value;
                },
                TimeSpan.FromSeconds(_cacheOptions.ExchangeRatesSeconds),
                cancellationToken).ConfigureAwait(false);

            return Result<ExchangeRatesSnapshot>.Success(snapshot);
        }
        catch (CachedFailureException exception)
        {
            return Result<ExchangeRatesSnapshot>.Failure(exception.Error);
        }
    }

    private async Task<Result<ExchangeRatesSnapshot>> FetchSnapshotAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseAddress, TickerPath));
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        try
        {
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Exchange rate upstream request failed with HTTP {StatusCode}.",
                    (int)response.StatusCode);
                return Result<ExchangeRatesSnapshot>.Failure(UnavailableError());
            }

            List<BinanceTickerPriceDto>? tickers;
            try
            {
                tickers = JsonSerializer.Deserialize<List<BinanceTickerPriceDto>>(body, SerializerOptions);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Exchange rate upstream returned an invalid payload.");
                return Result<ExchangeRatesSnapshot>.Failure(UnavailableError());
            }

            if (tickers is null)
            {
                _logger.LogWarning("Exchange rate upstream returned an empty payload.");
                return Result<ExchangeRatesSnapshot>.Failure(UnavailableError());
            }

            var prices = ReadPrices(tickers);
            if (!prices.TryGetValue(BitcoinSymbol, out var bitcoinUsdt)
                || !prices.TryGetValue(EthereumSymbol, out var ethereumUsdt)
                || !prices.TryGetValue(UsdCoinSymbol, out var usdCoinUsdt))
            {
                _logger.LogWarning("Exchange rate upstream omitted one or more required symbols.");
                return Result<ExchangeRatesSnapshot>.Failure(UnavailableError());
            }

            var rates = new Dictionary<CurrencyCode, decimal>
            {
                [CurrencyCode.USD] = 1m,
                [CurrencyCode.USDT] = 1m,
                [CurrencyCode.USDC] = 1m / usdCoinUsdt,
                [CurrencyCode.BTC] = 1m / bitcoinUsdt,
                [CurrencyCode.ETH] = 1m / ethereumUsdt,
            };

            return Result<ExchangeRatesSnapshot>.Success(
                new ExchangeRatesSnapshot(CurrencyCode.USD, rates, DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogWarning(exception, "Exchange rate upstream request timed out.");
            return Result<ExchangeRatesSnapshot>.Failure(UnavailableError());
        }
        catch (TimeoutRejectedException exception)
        {
            _logger.LogWarning(exception, "Exchange rate upstream request timed out.");
            return Result<ExchangeRatesSnapshot>.Failure(UnavailableError());
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Exchange rate upstream request did not complete.");
            return Result<ExchangeRatesSnapshot>.Failure(UnavailableError());
        }
    }

    private static Dictionary<string, decimal> ReadPrices(List<BinanceTickerPriceDto> tickers)
    {
        var prices = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var ticker in tickers)
        {
            if (string.IsNullOrWhiteSpace(ticker.Symbol) || string.IsNullOrWhiteSpace(ticker.Price))
            {
                continue;
            }

            if (!decimal.TryParse(
                    ticker.Price.Trim(),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var price)
                || price <= 0m)
            {
                continue;
            }

            prices[ticker.Symbol.Trim().ToUpperInvariant()] = price;
        }

        return prices;
    }

    private static Error UnavailableError() =>
        Error.ProviderUnavailable(
            "exchange_rates_unavailable",
            "Exchange rates are temporarily unavailable.");

    private sealed class CachedFailureException(Error error) : Exception(
        "The upstream result is a failure and must not be cached.")
    {
        public Error Error { get; } = error;
    }
}
