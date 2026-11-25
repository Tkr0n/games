using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Hybrid;
using VerkkuGames.Domain.ValueObjects;

namespace VerkkuGames.Api.Contracts;

/// <summary>
/// Canonical JSON contract shared by every public endpoint. It mirrors
/// <c>frontend/src/lib/types.ts</c>: camelCase properties, uppercase currency and
/// region codes, lowercase product types and snake_case requirement data types.
/// </summary>
public static class ApiJsonSerialization
{
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;

        options.Converters.Add(new MoneyJsonConverter());
        options.Converters.Add(new ProductTypeJsonConverter());
        options.Converters.Add(new RequirementDataTypeJsonConverter());
        options.Converters.Add(new RegionCodeJsonConverter());
        options.Converters.Add(new CurrencyCodeJsonConverter());
    }

    internal static JsonSerializerOptions CreateCacheSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        Configure(options);
        options.MakeReadOnly();
        return options;
    }

    private sealed class MoneyJsonConverter : JsonConverter<Money>
    {
        public override Money Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected a money object.");
            }

            var amount = 0m;
            var currency = CurrencyCode.USD;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return Money.Create(amount, currency);
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected a money property name.");
                }

                var propertyName = reader.GetString();
                reader.Read();

                if (string.Equals(propertyName, "amount", StringComparison.OrdinalIgnoreCase))
                {
                    amount = reader.GetDecimal();
                }
                else if (string.Equals(propertyName, "currency", StringComparison.OrdinalIgnoreCase))
                {
                    var rawCurrency = reader.TokenType == JsonTokenType.String
                        ? reader.GetString()
                        : null;

                    if (!Enum.TryParse<CurrencyCode>(rawCurrency?.Trim(), ignoreCase: true, out currency)
                        || !Enum.IsDefined(currency))
                    {
                        throw new JsonException("Unsupported currency code.");
                    }
                }
                else
                {
                    reader.Skip();
                }
            }

            throw new JsonException("Unexpected end of the money object.");
        }

        public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            writer.WriteStartObject();
            writer.WriteNumber("amount", value.Amount);
            writer.WriteString("currency", value.Currency.ToString());
            writer.WriteEndObject();
        }
    }

    private sealed class ProductTypeJsonConverter : JsonConverter<ProductType>
    {
        public override ProductType Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var raw = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            var normalized = raw?.Trim().Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal);

            if (string.Equals(normalized, "topup", StringComparison.OrdinalIgnoreCase))
            {
                return ProductType.TopUp;
            }

            if (string.Equals(normalized, "voucher", StringComparison.OrdinalIgnoreCase))
            {
                return ProductType.Voucher;
            }

            throw new JsonException("Unsupported product type.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            ProductType value,
            JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            writer.WriteStringValue(value == ProductType.Voucher ? "voucher" : "topup");
        }
    }

    private sealed class RequirementDataTypeJsonConverter : JsonConverter<RequirementDataType>
    {
        public override RequirementDataType Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var raw = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            var normalized = raw?.Trim().Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal);

            return normalized?.ToUpperInvariant() switch
            {
                "TEXT" => RequirementDataType.Text,
                "NUMBER" => RequirementDataType.Number,
                "SINGLESELECT" => RequirementDataType.SingleSelect,
                "MULTISELECT" => RequirementDataType.MultiSelect,
                _ => throw new JsonException("Unsupported requirement data type."),
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            RequirementDataType value,
            JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            writer.WriteStringValue(value switch
            {
                RequirementDataType.Number => "number",
                RequirementDataType.SingleSelect => "single_select",
                RequirementDataType.MultiSelect => "multi_select",
                _ => "text",
            });
        }
    }

    private sealed class RegionCodeJsonConverter : JsonConverter<RegionCode>
    {
        public override RegionCode Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var raw = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;

            return Enum.TryParse<RegionCode>(raw?.Trim(), ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed)
                    ? parsed
                    : throw new JsonException("Unsupported region code.");
        }

        public override void Write(Utf8JsonWriter writer, RegionCode value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            writer.WriteStringValue(value.ToString());
        }
    }

    private sealed class CurrencyCodeJsonConverter : JsonConverter<CurrencyCode>
    {
        public override CurrencyCode Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var raw = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;

            return Enum.TryParse<CurrencyCode>(raw?.Trim(), ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed)
                    ? parsed
                    : throw new JsonException("Unsupported currency code.");
        }

        public override CurrencyCode ReadAsPropertyName(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var raw = reader.GetString();

            return Enum.TryParse<CurrencyCode>(raw?.Trim(), ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed)
                    ? parsed
                    : throw new JsonException("Unsupported currency code.");
        }

        public override void Write(Utf8JsonWriter writer, CurrencyCode value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            writer.WriteStringValue(value.ToString());
        }

        public override void WriteAsPropertyName(
            Utf8JsonWriter writer,
            CurrencyCode value,
            JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            writer.WritePropertyName(value.ToString());
        }
    }
}

/// <summary>
/// Serializes cached values with the public API contract. HybridCache always
/// round-trips through bytes, so this keeps <c>Money</c> (immutable, get-only
/// properties) and the public enums intact in both cache tiers.
/// </summary>
public sealed class ApiJsonHybridCacheSerializer<T> : IHybridCacheSerializer<T>
{
    private static readonly JsonSerializerOptions SerializerOptions =
        ApiJsonSerialization.CreateCacheSerializerOptions();

    public T Deserialize(ReadOnlySequence<byte> source)
    {
        var reader = new Utf8JsonReader(source);
        var value = JsonSerializer.Deserialize<T>(ref reader, SerializerOptions);

        return value is null
            ? throw new JsonException("The cached payload was empty.")
            : value;
    }

    public void Serialize(T value, IBufferWriter<byte> target)
    {
        ArgumentNullException.ThrowIfNull(target);

        using var writer = new Utf8JsonWriter(target);
        JsonSerializer.Serialize(writer, value, SerializerOptions);
    }
}

/// <summary>
/// Applies <see cref="ApiJsonHybridCacheSerializer{T}"/> to every cached type.
/// </summary>
public sealed class ApiJsonHybridCacheSerializerFactory : IHybridCacheSerializerFactory
{
    public bool TryCreateSerializer<T>([MaybeNullWhen(false)] out IHybridCacheSerializer<T> serializer)
    {
        serializer = new ApiJsonHybridCacheSerializer<T>();
        return true;
    }
}
