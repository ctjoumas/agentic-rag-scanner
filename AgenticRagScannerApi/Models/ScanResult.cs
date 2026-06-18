using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Models;

/// <summary>
/// The aggregated result of a synchronous scan run, returned directly in the HTTP response (no
/// run-status polling for the POC). Carries the run identity and timing plus one
/// <see cref="TopicGroupResult"/> per selected topic group, in execution order.
/// </summary>
public sealed class ScanResult
{
    /// <summary>Unique identifier for this scan run.</summary>
    public required string RunId { get; init; }

    /// <summary>UTC timestamp the run started.</summary>
    public DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>UTC timestamp the run completed.</summary>
    public DateTimeOffset CompletedAtUtc { get; init; }

    /// <summary>Per-topic-group results, in the order the groups were executed.</summary>
    public required IReadOnlyList<TopicGroupResult> Groups { get; init; }
}
