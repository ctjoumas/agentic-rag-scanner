using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Pipeline;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Steps;

/// <inheritdoc />
public sealed class LoopController : ILoopController
{
    private readonly ILogger<LoopController> _logger;

    public LoopController(ILogger<LoopController> logger) => _logger = logger;

    public LoopDecision ReviewPass(TopicGroupContext context, IReadOnlyList<FetchedDocument> documents, ReviewDecision decision)
    {
        var pass = context.History.CurrentPass
            ?? throw new InvalidOperationException("ReviewPass was called before the current pass was started.");

        // Stub policy: honor the per-group maxLoops cap. Finalize once the cap is reached (the current
        // pass is already counted in LoopCount), otherwise retry. The real >80%-relevant accuracy
        // override is deferred to Epic 6.
        var capReached = context.LoopCount >= context.TopicGroup.MaxLoops;
        var finalDecision = capReached ? LoopDecision.Finalize : LoopDecision.Retry;
        var overridden = finalDecision != decision.Decision;

        var review = new Review
        {
            ThoughtProcess = decision.ThoughtProcess,
            LlmDecision = decision.Decision,
            FinalDecision = finalDecision,
            DecisionOverride = overridden,
            OverrideReason = overridden ? $"maxLoops cap ({context.TopicGroup.MaxLoops}) reached." : null,
        };

        foreach (var verdict in decision.Items)
        {
            if (verdict.Index < 0 || verdict.Index >= documents.Count)
            {
                continue;
            }

            var item = BuildResultItem(context, documents[verdict.Index], verdict, pass.Pass);

            if (verdict.Verdict == Verdict.NotRelevant)
            {
                review.Discarded.Add(item);
            }
            else
            {
                review.Vetted.Add(item);
            }
        }

        pass.Review = review;

        _logger.LogInformation(
            "Loop controller: group '{GroupId}' pass {Pass}/{MaxLoops} -> {Decision} (vetted {Vetted}, discarded {Discarded}).",
            context.TopicGroup.Id, pass.Pass, context.TopicGroup.MaxLoops, finalDecision, review.Vetted.Count, review.Discarded.Count);

        return finalDecision;
    }

    private static ResultItem BuildResultItem(TopicGroupContext context, FetchedDocument document, ItemVerdict verdict, int pass)
    {
        var hit = document.Hit;

        return new ResultItem
        {
            RunId = context.Run.RunId,
            GroupId = context.TopicGroup.Id,
            Id = StableId.FromUrl(hit.Url),
            SourceUrls = [hit.Url],
            Domain = hit.Domain,
            Verdict = verdict.Verdict,
            EvalRationale = verdict.Rationale,
            Unverified = document.Unverified,
            FoundOnPass = pass,
        };
    }
}
