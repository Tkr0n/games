using VerkkuGames.Api.Extensions;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Application.Rates.Queries;
using VerkkuGames.Domain.Pricing;
using VerkkuGames.Domain.ValueObjects;

namespace VerkkuGames.Api.Endpoints;

/// <summary>
/// Crypto rates by USD. Dictionary keys are emitted as the uppercase currency
/// codes the frontend expects (<c>ratesPerUsd.BTC</c>), never enum ordinals.
/// </summary>
public static class RatesEndpoints
{
    private const string GeneralRateLimitPolicy = "general";

    public static IEndpointRouteBuilder MapRatesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints
            .MapGet("/api/v1/rates", GetRatesAsync)
            .RequireRateLimiting(GeneralRateLimitPolicy);

        return endpoints;
    }

    private static async Task<IResult> GetRatesAsync(
        HttpContext httpContext,
        IQueryHandler<GetExchangeRatesQuery, ExchangeRatesSnapshot> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new GetExchangeRatesQuery(), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return result.ToHttpResult(httpContext);
        }

        var rates = new Dictionary<string, decimal>(
            result.Value.RatesPerUsd.Count,
            StringComparer.Ordinal);

        foreach (var (currency, rate) in result.Value.RatesPerUsd)
        {
            rates[currency.ToString()] = rate;
        }

        return Results.Ok(new ExchangeRatesResponse(
            result.Value.BaseCurrency,
            rates,
            result.Value.RetrievedAtUtc));
    }

    internal sealed record ExchangeRatesResponse(
        CurrencyCode BaseCurrency,
        IReadOnlyDictionary<string, decimal> RatesPerUsd,
        DateTimeOffset RetrievedAtUtc);
}
