using System.Text.Json.Serialization;

namespace AgenticRagScannerApi.Core.Contracts;

/// <summary>
/// Three-verdict relevance classification produced by the full-text relevance eval agent.
/// RELEVANT and BORDERLINE are both carried forward into enrichment (BORDERLINE is flagged);
/// NOT_RELEVANT is dropped and logged for audit (horizon-scanner-architecture.md, step 11).
/// Serialized by name so API responses and checkpoints are human-readable; the string converter
/// still reads legacy integer values for backward compatibility.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Verdict>))]
public enum Verdict
{
    /// <summary>On-topic and in-window; carried forward.</summary>
    Relevant,

    /// <summary>Uncertain (e.g. ambiguous date or weak topic match); carried forward but flagged.</summary>
    Borderline,

    /// <summary>Off-topic or out-of-window; dropped (logged for audit).</summary>
    NotRelevant
}
