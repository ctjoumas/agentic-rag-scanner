using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Pipeline;

namespace AgenticRagScannerApi.Workflows.Steps;

/// <summary>
/// Deterministic loop controller. After the relevance eval runs for a pass, it records the pass
/// <see cref="Review"/> (mapping per-item verdicts to vetted/discarded items) and decides whether to
/// loop again. The real policy (Epic 6, story 6.2) honors the per-group <see cref="TopicGroup.MaxLoops"/>
/// cap, respects the eval agent's goal-met judgement below the cap, and applies the >80% RELEVANT
/// recall override: when the eval wants to finalize but the pass was RELEVANT-rich, it loops again
/// (subject to the cap) on the assumption there is more primary-source material still to find.
/// </summary>
public interface ILoopController
{
    /// <summary>
    /// Records the review for the current pass and returns the loop decision. Assumes the current
    /// pass has already been appended to <see cref="SearchHistory.Passes"/>. Carried items' cleaned
    /// full text is snapshotted to blob storage as a side effect (provenance for the eval).
    /// </summary>
    Task<LoopDecision> ReviewPassAsync(TopicGroupContext context, IReadOnlyList<FetchedDocument> documents, ReviewDecision decision, CancellationToken cancellationToken = default);
}
