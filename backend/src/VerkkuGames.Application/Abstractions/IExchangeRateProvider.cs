using VerkkuGames.Domain.Common;
using VerkkuGames.Domain.Pricing;

namespace VerkkuGames.Application.Abstractions;

public interface IExchangeRateProvider
{
    Task<Result<ExchangeRatesSnapshot>> GetCryptoRatesAsync(CancellationToken cancellationToken = default);
}
