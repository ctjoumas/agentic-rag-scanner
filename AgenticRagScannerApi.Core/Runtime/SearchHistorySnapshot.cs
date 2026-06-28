using AgenticRagScannerApi.Core.Contracts;

namespace AgenticRagScannerApi.Core.Runtime;

/// <summary>
/// Flat, JSON-friendly snapshot of <see cref="SearchHistory"/>. The runtime type exposes computed
/// projections and init-only members, so it is mapped through these immutable DTOs. Two consumers share
/// this shape: MAF checkpoint state (resumability) and the per-group <see cref="TopicGroupResult.History"/>
/// returned at the end of a run (so a developer UI can replay every pass - query, hits, verdicts, and the
/// retry/finalize reasoning).
/// </summary>
public sealed record SearchHistorySnapshot(
    List<LoopPassSnapshot> Passes,
    List<string> ProcessedKeys);

/// <summary>Snapshot of one <see cref="LoopPass"/>.</summary>
public sealed record LoopPassSnapshot(
    int Pass,
    string Query,
    string? QueryRationale,
    List<SearchHit> Hits,
    ReviewSnapshot? Review);

/// <summary>Snapshot of one pass <see cref="Review"/>.</summary>
public sealed record ReviewSnapshot(
    string ThoughtProcess,
    LoopDecision LlmDecision,
    LoopDecision FinalDecision,
    bool DecisionOverride,
    string? OverrideReason,
    List<ResultItem> Vetted,
    List<ResultItem> Discarded);
