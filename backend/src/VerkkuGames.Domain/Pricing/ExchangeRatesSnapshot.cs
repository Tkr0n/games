using VerkkuGames.Domain.ValueObjects;

namespace VerkkuGames.Domain.Pricing;

public sealed record ExchangeRatesSnapshot(
    CurrencyCode BaseCurrency,
    IReadOnlyDictionary<CurrencyCode, decimal> RatesPerUsd,
    DateTimeOffset RetrievedAtUtc);
