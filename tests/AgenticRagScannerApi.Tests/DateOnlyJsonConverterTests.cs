using System.Text.Json;
using AgenticRagScannerApi.Models;
using AgenticRagScannerApi.Serialization;
using FluentAssertions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// The tolerant DateOnly converter accepts the canonical "yyyy-MM-dd" plus full ISO date-times
/// (so clients sending a timestamp for startDate/endDate don't fail), preserves null, and writes "yyyy-MM-dd".
/// </summary>
public class DateOnlyJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    [Theory]
    [InlineData("2025-06-15")]
    [InlineData("2025-06-15T00:00:00Z")]
    [InlineData("2025-06-15T23:30:00+05:00")]
    [InlineData("2025-06-15T08:15:42")]
    public void Deserialize_AcceptsDateAndDateTimeForms(string startDate)
    {
        var json = $$"""{"startDate":"{{startDate}}","jurisdiction":"United Kingdom","topicGroups":["Tax"]}""";

        var request = JsonSerializer.Deserialize<ScanRequest>(json, Options);

        request.Should().NotBeNull();
        request!.StartDate.Should().Be(new DateOnly(2025, 6, 15));
    }

    [Fact]
    public void Deserialize_PreservesNullStartDate()
    {
        var json = """{"startDate":null,"jurisdiction":"United Kingdom","topicGroups":["Tax"]}""";

        var request = JsonSerializer.Deserialize<ScanRequest>(json, Options);

        request.Should().NotBeNull();
        request!.StartDate.Should().BeNull();
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
