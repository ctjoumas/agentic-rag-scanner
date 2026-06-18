using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgenticRagScannerApi.Serialization;

/// <summary>
/// Tolerant <see cref="DateOnly"/> JSON converter. Reads the canonical ISO date ("yyyy-MM-dd") but
/// also accepts a full ISO date-time (e.g. "2025-06-15T00:00:00Z") by taking its date component, so
/// clients that send a timestamp for a date-only field don't fail. Always writes "yyyy-MM-dd".
/// Registered for <see cref="DateOnly"/> and applies to <see cref="Nullable{DateOnly}"/> too.
/// </summary>
public sealed class DateOnlyJsonConverter : JsonConverter<DateOnly>
{
    private const string DateFormat = "yyyy-MM-dd";

    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected an ISO date string but found token '{reader.TokenType}'.");
        }

        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException("Date value was null or empty.");
        }

        // Canonical date-only form first.
        if (DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        // Tolerate a full ISO date-time (with or without offset) by taking its date component.
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTimeOffset))
        {
            return DateOnly.FromDateTime(dateTimeOffset.Date);
        }

        throw new JsonException($"Could not convert '{value}' to a date. Use '{DateFormat}' (e.g. 2025-06-15).");
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(DateFormat, CultureInfo.InvariantCulture));
    }
}
