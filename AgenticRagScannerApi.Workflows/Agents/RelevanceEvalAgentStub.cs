using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Pipeline;
using AgenticRagScannerApi.Workflows.Prompts;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Epic 2 stub for <see cref="IRelevanceEvalAgent"/>: returns a canned <see cref="ReviewDecision"/>
/// with a per-document verdict - no LLM call. The canned distribution cycles Relevant / Borderline /
/// NotRelevant so the downstream Verdict Routing has both carried and discarded items to exercise.
/// The raw decision is always Retry; the deterministic Loop Controller owns the real stop condition
/// (maxLoops). Replaced by the real full-text agent in Epic 6.
/// </summary>
public sealed class RelevanceEvalAgentStub : IRelevanceEvalAgent
{
    private readonly ILogger<RelevanceEvalAgentStub> _logger;

    public RelevanceEvalAgentStub(ILogger<RelevanceEvalAgentStub> logger) => _logger = logger;

    public Task<ReviewDecision> EvaluateAsync(
        TopicGroupContext context,
        IReadOnlyList<FetchedDocument> documents,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "RelevanceEval stub ({PromptVersion}) for group '{GroupId}': {DocCount} document(s).",
            RelevanceEvalPrompt.Version, context.TopicGroup.Id, documents.Count);

        var items = new List<ItemVerdict>(documents.Count);
        for (var i = 0; i < documents.Count; i++)
        {
            var verdict = (i % 3) switch
            {
                0 => Verdict.Relevant,
                1 => Verdict.Borderline,
                _ => Verdict.NotRelevant,
            };

            items.Add(new ItemVerdict
            {
                Index = i,
                Verdict = verdict,
                Rationale = "Canned stub verdict (Epic 2) - no LLM call.",
            });
        }

        var decision = new ReviewDecision
        {
            ThoughtProcess = "Canned stub evaluation (Epic 2): no real LLM call; defer the stop decision to the Loop Controller.",
            Decision = LoopDecision.Retry,
            Items = items,
        };

        return Task.FromResult(decision);
    }
}
