namespace AgenticRagScannerApi.Core.Runtime;

/// <summary>
/// The aggregated result of a synchronous scan run: per-topic-group outcomes plus run-level timing.
/// Returned in the API response for the POC (synchronous request/response - no run-status polling).
/// </summary>
public sealed class ScanResult
{
    /// <summary>Unique identifier for this scan run.</summary>
    public required string RunId { get; init; }

    /// <summary>When the run started (UTC).</summary>
    public DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>When the run completed (UTC).</summary>
    public DateTimeOffset CompletedAtUtc { get; init; }

    /// <summary>Per-topic-group results, in execution order.</summary>
    public required IReadOnlyList<TopicGroupResult> Groups { get; init; }
}
