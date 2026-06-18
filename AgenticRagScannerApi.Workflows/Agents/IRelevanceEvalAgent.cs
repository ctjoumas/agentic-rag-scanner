using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Pipeline;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// LLM agent (stubbed in Epic 2): a single full-text relevance call. Classifies each current-pass
/// document RELEVANT / BORDERLINE / NOT_RELEVANT, is effective-date aware, and judges whether the
/// group's goal is met (which informs the loop decision). The real implementation lands in Epic 6 -
/// this interface freezes its I/O shape now.
/// </summary>
public interface IRelevanceEvalAgent
{
    /// <summary>Evaluates the current pass's fetched documents and returns a structured decision.</summary>
    Task<ReviewDecision> EvaluateAsync(
        TopicGroupContext context,
        IReadOnlyList<FetchedDocument> documents,
        CancellationToken cancellationToken = default);
}
