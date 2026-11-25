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
    [JsonPropertyName("big_categories")]
    public List<Shop2TopUpBigCategoryDto>? BigCategories { get; set; }
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

    [JsonPropertyName("image")]
    public string? Image { get; set; }
}

internal sealed class Shop2TopUpCategoriesResponse : Shop2TopUpResponseBase
{
    [JsonPropertyName("categories")]
    public List<Shop2TopUpCategoryDto>? Categories { get; set; }
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

    [JsonPropertyName("image")]
    public string? Image { get; set; }
}

internal sealed class Shop2TopUpSubcategoriesResponse : Shop2TopUpResponseBase
{
    [JsonPropertyName("subcategories")]
    public List<Shop2TopUpSubcategoryDto>? Subcategories { get; set; }
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

    [JsonPropertyName("price")]
    public string? Price { get; set; }

    [JsonPropertyName("prices_locked")]
    public bool? PricesLocked { get; set; }

    [JsonPropertyName("in_stock")]
    public bool? InStock { get; set; }

    [JsonPropertyName("product_type")]
    public string? ProductType { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }
}

internal sealed class Shop2TopUpPriceResponse : Shop2TopUpResponseBase
{
    [JsonPropertyName("price")]
    public Shop2TopUpPriceDto? Price { get; set; }
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

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("original_price")]
    public string? OriginalPrice { get; set; }

    [JsonPropertyName("discount_applied")]
    public bool? DiscountApplied { get; set; }

    [JsonPropertyName("in_stock")]
    public bool? InStock { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
}

internal sealed class Shop2TopUpRequirementsResponse : Shop2TopUpResponseBase
{
    [JsonPropertyName("requirements")]
    public List<Shop2TopUpRequirementDto>? Requirements { get; set; }
}

internal sealed class Shop2TopUpRequirementDto
{
    [JsonPropertyName("field_name")]
    public string? FieldName { get; set; }

    [JsonPropertyName("data_type")]
    public string? DataType { get; set; }

    [JsonPropertyName("placeholder")]
    public string? Placeholder { get; set; }

    [JsonPropertyName("select_options")]
    public List<JsonElement>? SelectOptions { get; set; }

    [JsonPropertyName("min_length")]
    public int? MinLength { get; set; }

    [JsonPropertyName("max_length")]
    public int? MaxLength { get; set; }
}

internal sealed class Shop2TopUpAccountResponse : Shop2TopUpResponseBase
{
    [JsonPropertyName("account")]
    public Shop2TopUpAccountDto? Account { get; set; }
}

internal sealed class Shop2TopUpAccountDto
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }
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
