using VerkkuGames.Application.Services;
using VerkkuGames.Domain.ValueObjects;
using Xunit;

namespace VerkkuGames.UnitTests.Application;

public sealed class RegionResolverTests
{
    private readonly RegionResolver _resolver = new();

    [Theory]
    [InlineData("PlayStation Store Gift Card USA", RegionCode.USA)]
    [InlineData("Xbox Gift Card United States", RegionCode.USA)]
    [InlineData("Tarjeta de regalo EEUU", RegionCode.USA)]
    [InlineData("Gift Card U.S.A.", RegionCode.USA)]
    [InlineData("Nintendo eShop Card US", RegionCode.USA)]
    [InlineData("Gift Card MX", RegionCode.MX)]
    [InlineData("Tarjeta de regalo México", RegionCode.MX)]
    [InlineData("Tarjeta de regalo Mexico", RegionCode.MX)]
    [InlineData("Panamá Gift Card", RegionCode.PA)]
    [InlineData("Panama Gift Card", RegionCode.PA)]
    [InlineData("Colombia Top Up", RegionCode.CO)]
    [InlineData("Global Gift Card", RegionCode.GLOBAL)]
    [InlineData("Worldwide Voucher", RegionCode.GLOBAL)]
    [InlineData("All Regions Credit", RegionCode.GLOBAL)]
    [InlineData("International Top Up", RegionCode.GLOBAL)]
    public void ResolveRegion_MatchesRegionTokens(string categoryName, RegionCode expected)
    {
        Assert.Equal(expected, _resolver.ResolveRegion(categoryName));
    }

    [Theory]
    [InlineData("Disco Elysium")]
    [InlineData("Spain Gift Card")]
    [InlineData("Focus Entertainment")]
    [InlineData("Discord Nitro")]
    [InlineData("Costa Rica Credit")]
    [InlineData("")]
    public void ResolveRegion_DoesNotFalseMatchSubstrings(string categoryName)
    {
        Assert.Equal(RegionCode.OTHER, _resolver.ResolveRegion(categoryName));
    }

    [Fact]
    public void ResolveRegion_FallsBackToItemName()
    {
        Assert.Equal(RegionCode.USA, _resolver.ResolveRegion("Gift Cards", "Steam Wallet Card US"));
    }

    [Fact]
    public void ResolveRegion_CategoryNameTakesPrecedenceOverItemName()
    {
        Assert.Equal(RegionCode.MX, _resolver.ResolveRegion("Gift Card MX", "Steam Wallet Card US"));
    }

    [Fact]
    public void ResolveRegion_NoMatchAnywhere_ReturnsOther()
    {
        Assert.Equal(RegionCode.OTHER, _resolver.ResolveRegion("Gift Cards", "Steam Wallet Card"));
    }

    [Fact]
    public void ResolveRegion_NullCategoryName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _resolver.ResolveRegion(null!));
    }

    [Fact]
    public void ExtractBaseTitle_NormalizesRegionalVariantsToSameTitle()
    {
        var usa = _resolver.ExtractBaseTitle("PlayStation Store Gift Card USA");
        var mexico = _resolver.ExtractBaseTitle("PlayStation Store Gift Card Mexico");

        Assert.Equal("playstation store gift card", usa);
        Assert.Equal(usa, mexico);
    }

    [Theory]
    [InlineData("Gift Card (USA)", "gift card")]
    [InlineData("Gift Card [Global]", "gift card")]
    [InlineData("Gift Card - Colombia", "gift card")]
    [InlineData("Gift Card | Panamá", "gift card")]
    [InlineData("  Gift   Card   MX  ", "gift card")]
    [InlineData("Tarjeta de Regalo EEUU", "tarjeta de regalo")]
    public void ExtractBaseTitle_RemovesRegionTokensBracketsAndSeparators(string input, string expected)
    {
        Assert.Equal(expected, _resolver.ExtractBaseTitle(input));
    }

    [Fact]
    public void ExtractBaseTitle_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _resolver.ExtractBaseTitle(null!));
    }
}
