using AgenticRagScannerApi.Core.Contracts;

namespace AgenticRagScannerApi.Core.Runtime;

/// <summary>
/// The high-level working object for one topic group within a run - one per MAF workflow instance.
/// Holds the shared run context, the group definition, and the in-memory <see cref="SearchHistory"/>.
/// </summary>
public sealed class TopicGroupContext
{
    /// <summary>Shared run-level context.</summary>
    public required RunContext Run { get; init; }

    /// <summary>The topic group this context scans.</summary>
    public required TopicGroup TopicGroup { get; init; }

    /// <summary>In-memory loop state (queries, passes, results) for this group.</summary>
    public SearchHistory History { get; } = new();

    /// <summary>Number of passes executed so far (derived from <see cref="SearchHistory.Passes"/>).</summary>
    public int LoopCount => History.Passes.Count;

    /// <summary>
    /// Loop predicate the MAF conditional uses: continue while under the per-group cap and the last
    /// pass asked to retry. The accuracy override is already baked into the recorded FinalDecision,
    /// so this just reads it. Defaults to "continue" before the first pass has been reviewed.
    /// </summary>
    public bool ShouldContinue() =>
        LoopCount < TopicGroup.MaxLoops &&
        (History.CurrentPass?.Review?.FinalDecision ?? LoopDecision.Retry) == LoopDecision.Retry;
}
