using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoticeSender.Core;
/// <summary>Swift JSONDecoder.iso8601 accepts UTC whole seconds on older macOS versions.</summary>
public sealed class MacIsoDateConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !DateTimeOffset.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            throw new JsonException("ISO8601 날짜 형식이 올바르지 않습니다.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
}
