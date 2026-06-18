using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Pipeline;

namespace AgenticRagScannerApi.Workflows.Steps;

/// <summary>
/// Deterministic loop controller (stub for Epic 2). After the relevance eval runs for a pass, it
/// records the pass <see cref="Review"/> (mapping per-item verdicts to vetted/discarded items) and
/// decides whether to loop again, honoring the per-group <see cref="TopicGroup.MaxLoops"/> cap. The
/// real controller (Epic 6) adds the goal-coverage and &gt;80%-relevant accuracy override.
/// </summary>
public interface ILoopController
{
    /// <summary>
    /// Records the review for the current pass and returns the loop decision. Assumes the current
    /// pass has already been appended to <see cref="SearchHistory.Passes"/>.
    /// </summary>
    LoopDecision ReviewPass(TopicGroupContext context, IReadOnlyList<FetchedDocument> documents, ReviewDecision decision);
}
