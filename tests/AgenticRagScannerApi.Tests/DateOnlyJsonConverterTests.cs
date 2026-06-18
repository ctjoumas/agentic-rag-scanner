using System.Text.Json;
using AgenticRagScannerApi.Models;
using AgenticRagScannerApi.Serialization;
using FluentAssertions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// The tolerant DateOnly converter accepts the canonical "yyyy-MM-dd" plus full ISO date-times
/// (so clients sending a timestamp for asOfDate don't fail), preserves null, and writes "yyyy-MM-dd".
/// </summary>
public class DateOnlyJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    [Theory]
    [InlineData("2025-06-15")]
    [InlineData("2025-06-15T00:00:00Z")]
    [InlineData("2025-06-15T23:30:00+05:00")]
    [InlineData("2025-06-15T08:15:42")]
    public void Deserialize_AcceptsDateAndDateTimeForms(string asOfDate)
    {
        var json = $$"""{"asOfDate":"{{asOfDate}}","jurisdiction":"United Kingdom","topicGroups":["Tax"]}""";

        var request = JsonSerializer.Deserialize<ScanRequest>(json, Options);

        request.Should().NotBeNull();
        request!.AsOfDate.Should().Be(new DateOnly(2025, 6, 15));
    }

    [Fact]
    public void Deserialize_PreservesNullAsOfDate()
    {
        var json = """{"asOfDate":null,"jurisdiction":"United Kingdom","topicGroups":["Tax"]}""";

        var request = JsonSerializer.Deserialize<ScanRequest>(json, Options);

        request.Should().NotBeNull();
        request!.AsOfDate.Should().BeNull();
    }

    [Fact]
    public void Serialize_WritesCanonicalIsoDate()
    {
        var json = JsonSerializer.Serialize(new DateOnly(2025, 6, 15), Options);

        json.Should().Be("\"2025-06-15\"");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new DateOnlyJsonConverter());
        return options;
    }
}
