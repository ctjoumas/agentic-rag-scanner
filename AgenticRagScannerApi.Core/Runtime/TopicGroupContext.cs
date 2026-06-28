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
    /// Loop predicate the MAF conditional uses. The recorded <see cref="Review.FinalDecision"/> is the
    /// primary signal: the LoopController has already baked both the recall override and the maxLoops cap
    /// into it (at the cap it always records Finalize), so reading it is sufficient. The
    /// <c>LoopCount &lt; MaxLoops</c> term is therefore redundant for correctness today - it is kept as a
    /// cheap defensive backstop that still guarantees termination of this paid agentic loop even if a
    /// future LoopController change failed to finalize at the cap. Defaults to "continue" before the first
    /// pass has been reviewed.
    /// </summary>
    public bool ShouldContinue() =>
        LoopCount < TopicGroup.MaxLoops &&
        (History.CurrentPass?.Review?.FinalDecision ?? LoopDecision.Retry) == LoopDecision.Retry;
}
