namespace AgenticRagScannerApi.Models;

/// <summary>
/// Acknowledgement returned when a scan is accepted. The <see cref="RunId"/> is
/// the anchor for the "one versioned result doc per item per run" model
/// (architecture-context.md §3, step 16) and ties together logs/exports for the run.
/// </summary>
public class ScanResponse
{
    /// <summary>Unique identifier for this scan run.</summary>
    public required string RunId { get; init; }

    /// <summary>Current run status. For the manual-trigger sprint this is "Accepted".</summary>
    public string Status { get; init; } = "Accepted";

    /// <summary>UTC timestamp the run was accepted.</summary>
    public DateTimeOffset AcceptedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Echo of the topic groups that will be fanned out to parallel workflows.</summary>
    public required IReadOnlyList<string> TopicGroups { get; init; }
}
