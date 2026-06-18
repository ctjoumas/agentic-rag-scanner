using AgenticRagScannerApi.Core.Contracts;

namespace AgenticRagScannerApi.Core.Runtime;

/// <summary>
/// One iteration ("pass") of the agentic RAG loop, self-contained for observability:
/// the query that was synthesized, the hits it returned, and the review/decision that followed.
/// Inspecting a pass is a single lookup - no parallel-array index correlation.
/// </summary>
public sealed class LoopPass
{
    /// <summary>1-based pass number.</summary>
    public required int Pass { get; init; }

    /// <summary>The single query synthesized for this pass.</summary>
    public required string Query { get; init; }

    /// <summary>Why this query was chosen / how it differs from prior passes.</summary>
    public string? QueryRationale { get; init; }

    /// <summary>Hits returned by Bing for this pass (after the deterministic pre-filter).</summary>
    public List<SearchHit> Hits { get; } = [];

    /// <summary>The review/eval outcome for this pass; null until the review step runs.</summary>
    public Review? Review { get; set; }
}
