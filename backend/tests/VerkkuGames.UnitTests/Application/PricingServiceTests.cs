using Microsoft.Extensions.Options;
using VerkkuGames.Application.Options;
using VerkkuGames.Application.Services;
using VerkkuGames.Domain.ValueObjects;
using Xunit;

namespace VerkkuGames.UnitTests.Application;

public sealed class PricingServiceTests
{
    private static PricingService CreateService(
        decimal defaultMarkupPercent = 10m,
        decimal minimumMarginUsd = 0.25m,
        Dictionary<string, decimal>? categoryOverrides = null) =>
        new(Options.Create(new PricingOptions
        {
            DefaultMarkupPercent = defaultMarkupPercent,
            MinimumMarginUsd = minimumMarginUsd,
            CategoryMarkupOverrides = categoryOverrides ?? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase),
        }));

    [Fact]
    public void CalculateRetailPrice_AppliesDefaultMarkup()
    {
        var service = CreateService();

        var price = service.CalculateRetailPrice(10.00m);

        Assert.Equal(11.00m, price.Amount);
        Assert.Equal(CurrencyCode.USD, price.Currency);
    }

    [Fact]
    public void CalculateRetailPrice_EnforcesMinimumMarginUsdFloor()
    {
        var service = CreateService();

        var price = service.CalculateRetailPrice(1.00m);

        // max(1.00 * 1.10, 1.00 + 0.25) => 1.25
        Assert.Equal(1.25m, price.Amount);
    }

    [Fact]
    public void CalculateRetailPrice_ExplicitZeroOverride_DoesNotForceMinimumMargin()
    {
        var service = CreateService(categoryOverrides: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["12"] = 0m,
        });

        var price = service.CalculateRetailPrice(1.00m, "12");

        Assert.Equal(1.00m, price.Amount);
    }

    [Fact]
    public void CalculateRetailPrice_PositiveCategoryOverride_ReplacesDefaultMarkup()
    {
        var service = CreateService(categoryOverrides: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["12"] = 20m,
        });

        var price = service.CalculateRetailPrice(10.00m, "12");

        Assert.Equal(12.00m, price.Amount);
    }

    [Fact]
    public void CalculateRetailPrice_CategoryOverrideLookupIsCaseInsensitive()
    {
        var service = CreateService(categoryOverrides: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["VOUCHERS"] = 20m,
        });

        var price = service.CalculateRetailPrice(10.00m, "vouchers");

        Assert.Equal(12.00m, price.Amount);
    }

    [Fact]
    public void CalculateRetailPrice_UnknownCategory_FallsBackToDefaultMarkup()
    {
        var service = CreateService(categoryOverrides: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["12"] = 20m,
        });

        var price = service.CalculateRetailPrice(10.00m, "999");

        Assert.Equal(11.00m, price.Amount);
    }

    [Fact]
    public void CalculateRetailPrice_BlankCategoryId_UsesDefaultMarkup()
    {
        var service = CreateService();

        Assert.Equal(11.00m, service.CalculateRetailPrice(10.00m, "  ").Amount);
        Assert.Equal(11.00m, service.CalculateRetailPrice(10.00m, null).Amount);
    }

    [Fact]
    public void CalculateRetailPrice_RoundsAwayFromZeroToTwoDecimals()
    {
        // 2.10 * 1.05 = 2.205 exactly -> AwayFromZero => 2.21 (banker's rounding would yield 2.20)
        var service = CreateService(defaultMarkupPercent: 5m, minimumMarginUsd: 0m);

        var price = service.CalculateRetailPrice(2.10m);

        Assert.Equal(2.21m, price.Amount);
    }

    [Fact]
    public void CalculateRetailPrice_NegativeProviderCost_ThrowsArgumentOutOfRangeException()
    {
        var service = CreateService();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.CalculateRetailPrice(-0.01m));
    }

    [Fact]
    public void CalculateRetailPrice_ZeroCostAndZeroMarkup_ReturnsZeroUsd()
    {
        var service = CreateService(defaultMarkupPercent: 0m, minimumMarginUsd: 0.25m);

        var price = service.CalculateRetailPrice(0m);

        Assert.Equal(0m, price.Amount);
        Assert.Equal(CurrencyCode.USD, price.Currency);
    }
}
