using System.Text.RegularExpressions;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Domain.ValueObjects;

namespace VerkkuGames.Application.Services;

public sealed partial class RegionResolver : IRegionResolver
{
    public RegionCode ResolveRegion(string categoryName, string? itemName = null)
    {
        ArgumentNullException.ThrowIfNull(categoryName);

        var categoryRegion = MatchRegion(categoryName);
        if (categoryRegion != RegionCode.OTHER)
        {
            return categoryRegion;
        }

        return string.IsNullOrWhiteSpace(itemName) ? RegionCode.OTHER : MatchRegion(itemName);
    }

    public string ExtractBaseTitle(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);

        var withoutRegion = RegionTokenPattern().Replace(categoryName, " ");
        var withoutEmptyBrackets = EmptyBracketPattern().Replace(withoutRegion, " ");
        var withoutSeparators = SeparatorPattern().Replace(withoutEmptyBrackets, " ");
        var collapsed = WhitespacePattern().Replace(withoutSeparators, " ").Trim();

        return collapsed.ToLowerInvariant();
    }

    private static RegionCode MatchRegion(string value)
    {
        if (UsaPattern().IsMatch(value))
        {
            return RegionCode.USA;
        }

        if (MexicoPattern().IsMatch(value))
        {
            return RegionCode.MX;
        }

        if (PanamaPattern().IsMatch(value))
        {
            return RegionCode.PA;
        }

        if (ColombiaPattern().IsMatch(value))
        {
            return RegionCode.CO;
        }

        return GlobalPattern().IsMatch(value) ? RegionCode.GLOBAL : RegionCode.OTHER;
    }

    [GeneratedRegex(
        @"\b(usa|us|united\s+states|eeuu|u\.s\.a\.?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UsaPattern();

    [GeneratedRegex(
        @"\b(mexico|m[eé]xico|mx)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MexicoPattern();

    [GeneratedRegex(
        @"\b(panama|panam[aá]|pa)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PanamaPattern();

    [GeneratedRegex(
        @"\b(colombia|co)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ColombiaPattern();

    [GeneratedRegex(
        @"\b(global|worldwide|ww|international|all\s+regions)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GlobalPattern();

    [GeneratedRegex(
        @"\b(usa|u\.s\.a\.?|us|united\s+states|eeuu|mexico|m[eé]xico|mx|panama|panam[aá]|pa|colombia|co|global|worldwide|ww|international|all\s+regions)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RegionTokenPattern();

    [GeneratedRegex(@"\(\s*\)|\[\s*\]", RegexOptions.CultureInvariant)]
    private static partial Regex EmptyBracketPattern();

    [GeneratedRegex(@"\s*[-–—|:]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
