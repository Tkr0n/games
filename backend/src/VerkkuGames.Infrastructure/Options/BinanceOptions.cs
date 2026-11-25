using System.ComponentModel.DataAnnotations;

namespace VerkkuGames.Infrastructure.Options;

public sealed class BinanceOptions
{
    public const string SectionName = "Binance";

    [Required]
    [Url]
    public string BaseUrl { get; set; } = "https://data-api.binance.vision";

    [Range(1, 30)]
    public int TimeoutSeconds { get; set; } = 5;
}
