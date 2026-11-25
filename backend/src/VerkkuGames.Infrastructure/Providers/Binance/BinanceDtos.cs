using System.Text.Json.Serialization;

namespace VerkkuGames.Infrastructure.Providers.Binance;

internal sealed class BinanceTickerPriceDto
{
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("price")]
    public string? Price { get; set; }
}
