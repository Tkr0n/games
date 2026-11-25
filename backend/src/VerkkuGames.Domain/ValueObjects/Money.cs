using System.Globalization;

namespace VerkkuGames.Domain.ValueObjects;

public readonly record struct Money
{
    public Money(decimal amount, CurrencyCode currency)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        Amount = Math.Round(amount, GetDecimalPlaces(currency), MidpointRounding.AwayFromZero);
        Currency = currency;
    }

    public decimal Amount { get; }

    public CurrencyCode Currency { get; }

    public static int GetDecimalPlaces(CurrencyCode currency) => currency switch
    {
        CurrencyCode.USD or CurrencyCode.USDT or CurrencyCode.USDC => 2,
        CurrencyCode.ETH => 6,
        CurrencyCode.BTC => 8,
        _ => throw new ArgumentOutOfRangeException(
            nameof(currency),
            currency,
            "The currency does not have a defined decimal precision."),
    };

    public static Money Create(decimal amount, CurrencyCode currency) => new(amount, currency);

    public static Money ParseInvariant(string rawAmount, CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(rawAmount);

        var trimmed = rawAmount.Trim();
        if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new FormatException($"'{rawAmount}' is not a valid invariant culture amount.");
        }

        return Create(parsed, currency);
    }

    public Money ConvertTo(CurrencyCode targetCurrency, decimal ratePerUsd)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ratePerUsd);

        if (Currency != CurrencyCode.USD)
        {
            throw new InvalidOperationException(
                $"Only amounts in {nameof(CurrencyCode.USD)} can be converted to another currency.");
        }

        return Create(Amount * ratePerUsd, targetCurrency);
    }
}
