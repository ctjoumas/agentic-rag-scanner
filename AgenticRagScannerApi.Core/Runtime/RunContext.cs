using System.Collections.Concurrent;

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

    /// <summary>Inclusive lower bound of the requested scan window (null = no lower cutoff).</summary>
    public DateOnly? StartDate { get; init; }

    /// <summary>Inclusive upper bound of the requested scan window (null = run start date is used).</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>Primary-source domain allowlist that scopes Bing search at query time.</summary>
    public required IReadOnlyList<string> AuthoritativeSources { get; init; }

    /// <summary>When the run started (UTC).</summary>
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Run-level set of normalized URL keys already surfaced by the deterministic pre-filter across
    /// <em>all</em> topic groups in this run (story 4.3 cross-group dedupe). This complements the
    /// per-group <see cref="SearchHistory.ProcessedKeys"/>: that one is checkpoint-backed and durable
    /// within a single group, while this one lives in memory only and prevents the same URL from being
    /// fetched and evaluated twice by different groups (the dominant cost driver). Backed by a
    /// concurrent collection so it stays correct under the parallel fan-out delivered in Epic 13.
    /// </summary>
    public ConcurrentDictionary<string, byte> SeenUrlKeys { get; } = new();
}
