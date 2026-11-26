using VerkkuGames.Domain.ValueObjects;

namespace VerkkuGames.Domain.Catalog;

/// <summary>
/// The license variant of a full-game key. <see langword="null"/> for top-ups and
/// gift cards; populated only for game-key providers (e.g. FazerCards).
/// </summary>
public enum GameKeyVariant
{
    BaseGame,
    Dlc,
    SeasonPass,
    Unknown,
}

/// <summary>
/// Geographic redemption restrictions for a game key. Empty lists mean the key
/// is region-free. Never expose provider names here — only ISO country codes.
/// </summary>
public sealed record RegionLockInfo(
    string RegionType,
    IReadOnlyList<string> AvailableCountryCodes,
    IReadOnlyList<string> UnavailableCountryCodes);
