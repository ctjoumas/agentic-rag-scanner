namespace AgenticRagScannerApi.Core.Runtime;

/// <summary>
/// Run-level context shared by every topic group in a single scan run.
/// </summary>
public sealed class RunContext
{
    /// <summary>Unique identifier for this scan run.</summary>
    public required string RunId { get; init; }

    /// <summary>Jurisdiction being scanned (e.g. "United Kingdom").</summary>
    public required string Jurisdiction { get; init; }

    /// <summary>Scan reference date / requested-window anchor.</summary>
    public DateOnly? AsOfDate { get; init; }

    /// <summary>Primary-source domain allowlist that scopes Bing search at query time.</summary>
    public required IReadOnlyList<string> AuthoritativeSources { get; init; }

    /// <summary>When the run started (UTC).</summary>
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
