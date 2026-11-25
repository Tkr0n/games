using VerkkuGames.Domain.ValueObjects;

namespace VerkkuGames.Application.Abstractions;

public interface IRegionResolver
{
    RegionCode ResolveRegion(string categoryName, string? itemName = null);

    string ExtractBaseTitle(string categoryName);
}
