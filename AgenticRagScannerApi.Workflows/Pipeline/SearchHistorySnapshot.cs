using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Pipeline;

/// <summary>
/// Flat, JSON-friendly snapshot of <see cref="SearchHistory"/> for MAF checkpointing. The runtime
/// type exposes computed projections and init-only members, so checkpoint state is mapped through
/// these DTOs (see <see cref="SearchHistorySerializer"/>).
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
