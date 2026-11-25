using System.Globalization;
using VerkkuGames.Domain.ValueObjects;
using Xunit;

namespace VerkkuGames.UnitTests.Domain;

public sealed class MoneyTests
{
    [Fact]
    public void GetDecimalPlaces_ReturnsPrecisionPerCurrency()
    {
        Assert.Equal(2, Money.GetDecimalPlaces(CurrencyCode.USD));
        Assert.Equal(2, Money.GetDecimalPlaces(CurrencyCode.USDT));
        Assert.Equal(2, Money.GetDecimalPlaces(CurrencyCode.USDC));
        Assert.Equal(6, Money.GetDecimalPlaces(CurrencyCode.ETH));
        Assert.Equal(8, Money.GetDecimalPlaces(CurrencyCode.BTC));
    }

    [Fact]
    public void Create_NegativeAmount_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Create(-0.01m, CurrencyCode.USD));
    }

    [Fact]
    public void Create_RoundsAwayFromZero_ByCurrencyPrecision()
    {
        Assert.Equal(1.01m, Money.Create(1.005m, CurrencyCode.USD).Amount);
        Assert.Equal(2.35m, Money.Create(2.345m, CurrencyCode.USDT).Amount);
        Assert.Equal(1.000001m, Money.Create(1.0000005m, CurrencyCode.ETH).Amount);
        Assert.Equal(1.00000001m, Money.Create(1.000000005m, CurrencyCode.BTC).Amount);
    }

    [Fact]
    public void Create_PreservesCurrencyAndLargestAllowedAmount()
    {
        var money = Money.Create(0.50m, CurrencyCode.BTC);

        Assert.Equal(0.5m, money.Amount);
        Assert.Equal(CurrencyCode.BTC, money.Currency);
    }

    [Fact]
    public void ParseInvariant_UnderSpanishLocale_ParsesDotDecimalString()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-VE");

            var money = Money.ParseInvariant("0.950000", CurrencyCode.USD);

            Assert.Equal(0.95m, money.Amount);
            Assert.Equal(CurrencyCode.USD, money.Currency);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void ParseInvariant_TrimsWhitespace_RoundsAndRejectsInvalidInput()
    {
        Assert.Equal(0.95m, Money.ParseInvariant("  0.950000  ", CurrencyCode.USD).Amount);
        Assert.Throws<FormatException>(() => Money.ParseInvariant("not-a-number", CurrencyCode.USD));
    }

    [Fact]
    public void ConvertTo_SmallUsdToBtc_PreservesEightDecimalsWithoutZeroing()
    {
        var usd = Money.Create(0.50m, CurrencyCode.USD);

        var btc = usd.ConvertTo(CurrencyCode.BTC, 1m / 96420.15m);

        Assert.Equal(0.00000519m, btc.Amount);
        Assert.Equal(CurrencyCode.BTC, btc.Currency);
    }

    [Fact]
    public void ConvertTo_NonPositiveRate_ThrowsArgumentOutOfRangeException()
    {
        var usd = Money.Create(1m, CurrencyCode.USD);

        Assert.Throws<ArgumentOutOfRangeException>(() => usd.ConvertTo(CurrencyCode.ETH, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => usd.ConvertTo(CurrencyCode.ETH, -1m));
    }

    [Fact]
    public void ConvertTo_NonUsdSource_ThrowsInvalidOperationException()
    {
        var btc = Money.Create(1m, CurrencyCode.BTC);

        Assert.Throws<InvalidOperationException>(() => btc.ConvertTo(CurrencyCode.USD, 96420.15m));
    }

    [Fact]
    public void RecordStruct_EqualityComparesAmountAndCurrency()
    {
        Assert.Equal(Money.Create(1.005m, CurrencyCode.USD), Money.Create(1.01m, CurrencyCode.USD));
        Assert.NotEqual(Money.Create(1.01m, CurrencyCode.USD), Money.Create(1.01m, CurrencyCode.BTC));
    }
}
