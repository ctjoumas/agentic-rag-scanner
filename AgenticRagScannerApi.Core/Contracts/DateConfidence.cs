using System.Text.Json.Serialization;

namespace AgenticRagScannerApi.Core.Contracts;

/// <summary>
/// Confidence the eval agent has in the dates it extracted from an item's full text.
/// Low/Unknown leans an item toward BORDERLINE with an unverified-date flag rather than dropping it
/// (horizon-scanner-architecture.md, "What 'effective-date aware' means in the eval agent").
/// Serialized by name so API responses and checkpoints are human-readable; the string converter
/// still reads legacy integer values for backward compatibility.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DateConfidence>))]
public enum DateConfidence
{
    Unknown = 0,
    Low,
    Medium,
    High
}
