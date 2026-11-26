namespace VerkkuGames.Domain.Catalog;

/// <summary>
/// Optional game metadata used by storefront filters. Null when the provider has
/// no metadata (e.g. no Steam app id) — the UI never shows invented values.
/// </summary>
public sealed record GameMetadata(
    int? ReleaseYear,
    IReadOnlyList<string> Genres,
    bool IsMultiplayer,
    IReadOnlyList<string> SupportedPlatforms,
    string? Developer);
