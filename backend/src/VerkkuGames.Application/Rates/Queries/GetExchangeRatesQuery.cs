using VerkkuGames.Application.Abstractions;
using VerkkuGames.Domain.Common;
using VerkkuGames.Domain.Pricing;

namespace VerkkuGames.Application.Rates.Queries;

public sealed record GetExchangeRatesQuery;

public sealed class GetExchangeRatesQueryHandler(IExchangeRateProvider exchangeRateProvider)
    : IQueryHandler<GetExchangeRatesQuery, ExchangeRatesSnapshot>
{
    public Task<Result<ExchangeRatesSnapshot>> HandleAsync(
        GetExchangeRatesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return exchangeRateProvider.GetCryptoRatesAsync(cancellationToken);
    }
}
