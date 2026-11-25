using System.Diagnostics.CodeAnalysis;
using VerkkuGames.Application.Abstractions;

namespace VerkkuGames.Api.Endpoints;

/// <summary>
/// Liveness is unconditional; readiness verifies the cache and every configured
/// catalog provider. Both responses are status-only: no provider names, keys,
/// emails, wallet balances or connection strings can ever surface here.
/// </summary>
public static class HealthEndpoints
{
    private const string ReadinessCacheKey = "health:ready";

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/health/live", GetLive);
        endpoints.MapGet("/health/ready", GetReadyAsync);

        return endpoints;
    }

    private static IResult GetLive() =>
        Results.Json(new HealthStatusResponse("ok"));

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A readiness probe must degrade to 503 instead of surfacing a 500.")]
    private static async Task<IResult> GetReadyAsync(
        ICatalogProviderRegistry providerRegistry,
        ICacheService cache,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerRegistry);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var logger = loggerFactory.CreateLogger("VerkkuGames.Api.Health");

        try
        {
            var cacheProbe = await cache
                .GetOrCreateAsync(
                    ReadinessCacheKey,
                    _ => new ValueTask<string>("ok"),
                    TimeSpan.FromSeconds(5),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!string.Equals(cacheProbe, "ok", StringComparison.Ordinal))
            {
                return Degraded();
            }

            foreach (var provider in providerRegistry.GetAll())
            {
                var health = await provider
                    .CheckHealthAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (health.IsFailure || !health.Value)
                {
                    return Degraded();
                }
            }

            return Results.Json(new HealthStatusResponse("ready"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Readiness probe failed.");
            return Degraded();
        }
    }

    private static IResult Degraded() =>
        Results.Json(
            new HealthStatusResponse("degraded"),
            statusCode: StatusCodes.Status503ServiceUnavailable);

    internal sealed record HealthStatusResponse(string Status);
}
