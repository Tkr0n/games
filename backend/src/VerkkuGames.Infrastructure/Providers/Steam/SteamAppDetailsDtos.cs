using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using VerkkuGames.Domain.Catalog;

namespace VerkkuGames.Infrastructure.Providers.Steam;

internal sealed class SteamAppDetailsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public SteamAppDetailsDto? Data { get; set; }
}

internal sealed class SteamAppDetailsDto
{
    [JsonPropertyName("release_date")]
    public SteamReleaseDateDto? ReleaseDate { get; set; }

    [JsonPropertyName("genres")]
    public List<SteamNamedDto>? Genres { get; set; }

    [JsonPropertyName("categories")]
    public List<SteamNamedDto>? Categories { get; set; }

    [JsonPropertyName("platforms")]
    public SteamPlatformsDto? Platforms { get; set; }

    [JsonPropertyName("developers")]
    public List<string>? Developers { get; set; }
}

internal sealed class SteamReleaseDateDto
{
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("coming_soon")]
    public bool ComingSoon { get; set; }
}

internal sealed class SteamNamedDto
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal sealed class SteamPlatformsDto
{
    [JsonPropertyName("windows")]
    public bool Windows { get; set; }

    [JsonPropertyName("mac")]
    public bool Mac { get; set; }

    [JsonPropertyName("linux")]
    public bool Linux { get; set; }
}

internal static class SteamAppDetailsParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static GameMetadata? Parse(string json, long appId)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(appId.ToString(CultureInfo.InvariantCulture), out var wrapper))
            {
                return null;
            }

            var envelope = wrapper.Deserialize<SteamAppDetailsResponse>(Options);
            if (envelope is not { Success: true, Data: { } data })
            {
                return null;
            }

            var year = ParseYear(data.ReleaseDate?.Date);
            var genres = data.Genres?
                .Where(genre => !string.IsNullOrWhiteSpace(genre.Description))
                .Select(genre => genre.Description!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];

            var multiplayer = data.Categories?.Any(category =>
                category.Description is { } description &&
                (description.Contains("multi-player", StringComparison.OrdinalIgnoreCase)
                 || description.Contains("co-op", StringComparison.OrdinalIgnoreCase)
                 || description.Contains("pvp", StringComparison.OrdinalIgnoreCase))) == true;

            var platforms = new List<string>();
            if (data.Platforms?.Windows == true) platforms.Add("Windows");
            if (data.Platforms?.Mac == true) platforms.Add("macOS");
            if (data.Platforms?.Linux == true) platforms.Add("Linux");

            var developer = data.Developers?
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

            return new GameMetadata(year, genres, multiplayer, platforms, developer);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ParseYear(string? rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate)) return null;

        // Steam dates look like "Jul 25, 2024" or "25 Jul, 2024"; a 4-digit
        // year is the only stable signal across locales.
        for (var index = rawDate.Length - 4; index >= 0; index--)
        {
            var candidate = rawDate.Substring(index, 4);
            if (int.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                && year is >= 1970 and <= 2100)
            {
                return year;
            }
        }

        return null;
    }
}
