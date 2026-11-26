using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VerkkuGames.Infrastructure.Providers.Shop2TopUp;

internal class Shop2TopUpResponseBase
{
    [JsonPropertyName("success")]
    public bool? Success { get; set; }

    [JsonPropertyName("error")]
    public Shop2TopUpErrorDto? Error { get; set; }
}

internal sealed class Shop2TopUpErrorDto
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("retry_after")]
    public int? RetryAfter { get; set; }
}

internal sealed class Shop2TopUpBigCategoriesResponse : Shop2TopUpResponseBase
{
    [JsonPropertyName("data")]
    public List<Shop2TopUpBigCategoryDto>? Data { get; set; }
}

internal sealed class Shop2TopUpBigCategoryDto
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(StringOrNumberJsonConverter))]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }
}

internal sealed class Shop2TopUpCategoriesResponse : Shop2TopUpResponseBase
{
    [JsonPropertyName("data")]
    public List<Shop2TopUpCategoryDto>? Data { get; set; }
}

internal sealed class Shop2TopUpCategoryDto
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(StringOrNumberJsonConverter))]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("big_category_id")]
    [JsonConverter(typeof(StringOrNumberJsonConverter))]
    public string? BigCategoryId { get; set; }

    [JsonPropertyName("big_category_name")]
    public string? BigCategoryName { get; set; }

    [JsonPropertyName("region_ids")]
    public List<Shop2TopUpRegionRefDto>? RegionIds { get; set; }

    [JsonPropertyName("country_ids")]
    public List<Shop2TopUpCountryRefDto>? CountryIds { get; set; }

    [JsonPropertyName("requirements")]
    public JsonElement? Requirements { get; set; }

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }
}

internal sealed class Shop2TopUpRegionRefDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class Shop2TopUpCountryRefDto
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }
}

internal sealed class Shop2TopUpSubcategoriesResponse : Shop2TopUpResponseBase
{
    [JsonPropertyName("data")]
    public List<Shop2TopUpSubcategoryDto>? Data { get; set; }
}

internal sealed class Shop2TopUpSubcategoryDto
{
    [JsonPropertyName("item_id")]
    [JsonConverter(typeof(StringOrNumberJsonConverter))]
    public string? ItemId { get; set; }

    [JsonPropertyName("id")]
    [JsonConverter(typeof(StringOrNumberJsonConverter))]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("category_id")]
    [JsonConverter(typeof(StringOrNumberJsonConverter))]
    public string? CategoryId { get; set; }

    [JsonPropertyName("category_name")]
    public string? CategoryName { get; set; }

    [JsonPropertyName("product_type")]
    public string? ProductType { get; set; }

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("in_stock")]
    public bool? InStock { get; set; }
}

internal sealed class Shop2TopUpPriceResponse : Shop2TopUpResponseBase
{
    [JsonPropertyName("data")]
    public Shop2TopUpPriceDto? Data { get; set; }
}

internal sealed class Shop2TopUpPriceDto
{
    [JsonPropertyName("item_id")]
    [JsonConverter(typeof(StringOrNumberJsonConverter))]
    public string? ItemId { get; set; }

    [JsonPropertyName("item_name")]
    public string? ItemName { get; set; }

    [JsonPropertyName("unit_price")]
    public string? UnitPrice { get; set; }

    [JsonPropertyName("in_stock")]
    public bool? InStock { get; set; }
}

internal sealed class Shop2TopUpAccountResponse : Shop2TopUpResponseBase
{
    [JsonPropertyName("account")]
    public Shop2TopUpAccountDto? Account { get; set; }
}

internal sealed class Shop2TopUpAccountDto
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("wallet")]
    public string? Wallet { get; set; }
}

internal sealed class StringOrNumberJsonConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number when reader.TryGetInt64(out var number) =>
                number.ToString(CultureInfo.InvariantCulture),
            JsonTokenType.Number => reader.GetDecimal().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.Null => null,
            _ => throw new JsonException("Expected a string or a number identifier."),
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value);
    }
}
