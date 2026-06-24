using System.Text.Json.Serialization;

namespace AgenticRagScannerApi.Core.Contracts;

/// <summary>
/// Outcome of a single agentic-loop pass: keep searching or stop.
/// Drives the loop controller / MAF conditional (horizon-scanner-architecture.md, step 10).
/// Serialized by name ("Retry"/"Finalize") so API responses and checkpoints are human-readable;
/// the string converter still reads legacy integer values for backward compatibility.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<LoopDecision>))]
public enum LoopDecision
{
    /// <summary>Goal not yet met - synthesize a new, non-redundant query and run another pass.</summary>
    Retry,

    /// <summary>Enough data gathered - exit the loop and route verdicts downstream.</summary>
    Finalize
}
