using AgenticRagScannerApi.Core.Contracts;

namespace AgenticRagScannerApi.Core.Runtime;

/// <summary>
/// The aggregated outcome of scanning a single topic group within a run. Produced by the per-group
/// execution step - a stub in Phase 1, replaced by the real MAF workflow in Epic 2 - and collected
/// into the run-level <see cref="ScanResult"/>.
/// </summary>
public sealed class TopicGroupResult
{
    /// <summary>Identifier of the topic group this result belongs to.</summary>
    public required string GroupId { get; init; }

    /// <summary>Human-readable topic group name.</summary>
    public required string GroupName { get; init; }

    /// <summary>Terminal status for the group (e.g. "Completed").</summary>
    public required string Status { get; init; }

    /// <summary>Number of loop passes the group executed.</summary>
    public int LoopCount { get; init; }

    /// <summary>Vetted items surfaced for this group (empty for the Phase 1 stub).</summary>
    public IReadOnlyList<ResultItem> Items { get; init; } = [];

    /// <summary>
    /// The Deloitte View for this topic group (Epic 8, story 8.5): a single record - shaped to match the
    /// historical <c>RegulatoryUpdatesCsv</c> (<see cref="DeloitteViewRecord"/>) - that <em>aggregates</em>
    /// the group's carried regulatory updates (their summaries, impact areas, and tags) into one
    /// practitioner Deloitte View, grounded in the topic group and steered by prior Deloitte Views for the
    /// run's jurisdiction. Null when the group carried no items.
    /// </summary>
    public DeloitteViewRecord? DeloitteView { get; set; }

    /// <summary>
    /// Full per-pass history of the agentic RAG loop for this group: each pass's query and rationale,
    /// the hits it retrieved, and the review (thought process, LLM vs. final decision, any override and
    /// its reason, plus the vetted and discarded items). Lets a developer UI peek into every pass.
    /// Null for the Phase 1 stub, which runs no workflow loop.
    /// </summary>
    public SearchHistorySnapshot? History { get; init; }
}