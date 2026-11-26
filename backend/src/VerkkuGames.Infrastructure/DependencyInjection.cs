using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly.Timeout;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Application.Options;
using VerkkuGames.Infrastructure.Caching;
using VerkkuGames.Infrastructure.Options;
using VerkkuGames.Infrastructure.Providers.Binance;
using VerkkuGames.Infrastructure.Providers.FazerCards;
using VerkkuGames.Infrastructure.Providers.Shop2TopUp;

namespace VerkkuGames.Infrastructure;

public static class DependencyInjection
{
    private const int RetryMaxAttempts = 2;

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<PricingOptions>()
            .Bind(configuration.GetSection(PricingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<Shop2TopUpOptions>()
            .Bind(configuration.GetSection(Shop2TopUpOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<BinanceOptions>()
            .Bind(configuration.GetSection(BinanceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CacheOptions>()
            .Bind(configuration.GetSection(CacheOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<FazerCardsOptions>()
            .Bind(configuration.GetSection(FazerCardsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var cacheOptions = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>()
            ?? new CacheOptions();

        services.AddHybridCache();

        if (cacheOptions.UseRedis && !string.IsNullOrWhiteSpace(cacheOptions.RedisConnectionString))
        {
            services.AddStackExchangeRedisCache(options => options.Configuration = cacheOptions.RedisConnectionString);
        }

        services.AddSingleton<ICacheService, RedisHybridCacheService>();

        var shop2TopUpOptions = configuration.GetSection(Shop2TopUpOptions.SectionName).Get<Shop2TopUpOptions>()
            ?? new Shop2TopUpOptions();
        var binanceOptions = configuration.GetSection(BinanceOptions.SectionName).Get<BinanceOptions>()
            ?? new BinanceOptions();

        services.AddHttpClient<Shop2TopUpCatalogProvider>(client =>
            {
                client.BaseAddress = BuildBaseAddress(shop2TopUpOptions.BaseUrl);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddStandardResilienceHandler(options =>
                ConfigureResilience(options, shop2TopUpOptions.TimeoutSeconds));

        services.AddTransient<ICatalogProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<Shop2TopUpCatalogProvider>());

        var fazerCardsOptions = configuration.GetSection(FazerCardsOptions.SectionName).Get<FazerCardsOptions>()
            ?? new FazerCardsOptions();
        if (!string.IsNullOrWhiteSpace(fazerCardsOptions.ApiKey))
        {
            services.AddHttpClient<FazerCardsCatalogProvider>(client =>
                {
                    client.BaseAddress = BuildBaseAddress(fazerCardsOptions.BaseUrl);
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                })
                .AddStandardResilienceHandler(options =>
                    ConfigureResilience(options, fazerCardsOptions.TimeoutSeconds));

            services.AddTransient<ICatalogProvider>(serviceProvider =>
                serviceProvider.GetRequiredService<FazerCardsCatalogProvider>());
        }

        services.AddHttpClient<BinanceExchangeRateProvider>(client =>
            {
                client.BaseAddress = BuildBaseAddress(binanceOptions.BaseUrl);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddStandardResilienceHandler(options =>
                ConfigureResilience(options, binanceOptions.TimeoutSeconds));

        services.AddTransient<IExchangeRateProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<BinanceExchangeRateProvider>());

        return services;
    }

    private static void ConfigureResilience(
        HttpStandardResilienceOptions options,
        int timeoutSeconds)
    {
        options.Retry.MaxRetryAttempts = RetryMaxAttempts;

        // Retry ONLY transient failures: timeouts, 408 and 5xx. Never 400/401/403/404/409/429.
        options.Retry.ShouldHandle = arguments =>
        {
            var outcome = arguments.Outcome;
            if (outcome.Exception is { } exception)
            {
                return ValueTask.FromResult(
                    exception is HttpRequestException or TimeoutException or TimeoutRejectedException);
            }

            return ValueTask.FromResult(
                outcome.Result is { } response && IsTransientStatusCode(response.StatusCode));
        };

        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(timeoutSeconds * (RetryMaxAttempts + 2));
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(Math.Max(30, timeoutSeconds * 2));
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout || (int)statusCode >= 500;

    private static Uri BuildBaseAddress(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        return new Uri(string.Concat(baseUrl.TrimEnd('/'), "/"), UriKind.Absolute);
    }
}
