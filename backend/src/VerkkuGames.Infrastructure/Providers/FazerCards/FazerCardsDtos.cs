using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VerkkuGames.Infrastructure.Providers.FazerCards;

internal class FazerCardsResponseBase
{
    [JsonPropertyName("ok")]
    public bool? Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }
}

internal sealed class FazerCardsErrorDto
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }
}

internal sealed class FazerCardsGameCatalogResponse : FazerCardsResponseBase
{
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("items")]
    public List<FazerCardsGameDto>? Items { get; set; }

    [JsonPropertyName("meta")]
    public FazerCardsMetaDto? Meta { get; set; }
}

internal sealed class FazerCardsGameDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("game_id")]
    public string? GameId { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("region_restriction")]
    public bool RegionRestriction { get; set; }

    [JsonPropertyName("appid")]
    public long? AppId { get; set; }

    [JsonPropertyName("imageurl")]
    public string? ImageUrl { get; set; }
}

internal sealed class FazerCardsMetaDto
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; set; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }
}

internal sealed class FazerCardsKeysResponse : FazerCardsResponseBase
{
    [JsonPropertyName("game_id")]
    public string? GameId { get; set; }

    // Historical field name kept capitalized by the upstream API.
    [JsonPropertyName("GameName")]
    public string? GameName { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("region_restriction")]
    public bool RegionRestriction { get; set; }

    [JsonPropertyName("appid")]
    public long? AppId { get; set; }

    [JsonPropertyName("imageurl")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("keys")]
    public List<FazerCardsKeyDto>? Keys { get; set; }
}

internal sealed class FazerCardsKeyDto
{
    [JsonPropertyName("key_id")]
    public string? KeyId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("price_usd")]
    [JsonConverter(typeof(InvariantDecimalStringConverter))]
    public decimal? PriceUsd { get; set; }

    [JsonPropertyName("stock")]
    public int Stock { get; set; }
}

internal sealed class FazerCardsRegionRestrictionResponse : FazerCardsResponseBase
{
    [JsonPropertyName("region_type")]
    public string? RegionType { get; set; }

    [JsonPropertyName("has_availability")]
    public bool HasAvailability { get; set; }

    [JsonPropertyName("available")]
    public List<FazerCardsCountryDto>? Available { get; set; }

    [JsonPropertyName("unavailable")]
    public List<FazerCardsCountryDto>? Unavailable { get; set; }
}

internal sealed class FazerCardsCountryDto
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class FazerCardsAccountResponse : FazerCardsResponseBase
{
    [JsonPropertyName("login")]
    public string? Login { get; set; }
}

internal sealed class InvariantDecimalStringConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String when decimal.TryParse(
                reader.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var fromString) => fromString,
            JsonTokenType.Number when reader.TryGetDecimal(out var fromNumber) => fromNumber,
            JsonTokenType.Null => null,
            _ => null,
        };

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value is { } number)
        {
            writer.WriteStringValue(number.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
