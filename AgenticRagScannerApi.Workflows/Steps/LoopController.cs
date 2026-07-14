using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Common;
using AgenticRagScannerApi.Workflows.Pipeline;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace AgenticRagScannerApi.Workflows.Steps;

/// <inheritdoc />
public sealed class LoopController : ILoopController
{
    /// <summary>
    /// Recall-override threshold: when the eval agent wants to finalize but at least this share of
    /// the pass's evaluated items are RELEVANT, a vein this rich suggests there is likely more primary-source
    /// material still to find, so we override into another RETRY rather than stop early (recall-first - a
    /// false negative is the costly error in compliance). The maxLoops cap still bounds the total passes
    /// (horizon-scanner-architecture.md, step 10 - the ">80% RELEVANT" override).
    /// </summary>
    private const double RelevantRetryThreshold = 0.80;

    private readonly IFullTextStore _fullTextStore;
    private readonly ILogger<LoopController> _logger;

    public LoopController(IFullTextStore fullTextStore, ILogger<LoopController> logger)
    {
        _fullTextStore = fullTextStore;
        _logger = logger;
    }

    public async Task<LoopDecision> ReviewPassAsync(
        TopicGroupContext context,
        IReadOnlyList<FetchedDocument> documents,
        ReviewDecision decision,
        CancellationToken cancellationToken = default)
    {
        var pass = context.History.CurrentPass
            ?? throw new InvalidOperationException("ReviewPass was called before the current pass was started.");

        // Real loop policy (Epic 6, story 6.2):
        //  - The per-group maxLoops cap is the hard stop: at the cap we always finalize.
        //  - Below the cap we honor the eval agent's judgement (goal met -> Finalize, goal unmet -> Retry).
        //    The eval already retries rather than finalizing when a pass's web search FAILED (an error
        //    state, not a genuine empty result), so a broken search does not end the group early here.
        //    UNLESS the recall override fires: when the eval wants to finalize but the pass was >80%
        //    RELEVANT, a vein this rich suggests there is more primary-source material to find, so we
        //    override into another RETRY (still bounded by maxLoops) rather than risk missing it.
        var capReached = context.LoopCount >= context.TopicGroup.MaxLoops;
        var relevantShare = ComputeRelevantShare(decision.Items);

        LoopDecision finalDecision;
        string? overrideReason;

        if (capReached)
        {
            finalDecision = LoopDecision.Finalize;
            overrideReason = decision.Decision == LoopDecision.Retry
                ? $"maxLoops cap ({context.TopicGroup.MaxLoops}) reached."
                : null;
        }
        else if (decision.Decision == LoopDecision.Finalize && relevantShare >= RelevantRetryThreshold)
        {
            finalDecision = LoopDecision.Retry;
            overrideReason = $"recall override: {relevantShare:P0} of items RELEVANT (>= {RelevantRetryThreshold:P0}); searching again for more.";
        }
        else
        {
            // Under the cap with no override: honor the eval agent's decision.
            finalDecision = decision.Decision;
            overrideReason = null;
        }

        var overridden = finalDecision != decision.Decision;

        // The scan's date range is the in-scope window: an item confidently published BEFORE the start
        // date or AFTER the end date is out of scope regardless of the eval verdict. We only apply this to
        // items the eval carried forward, and only when the publication date is confidently outside the
        // window - undated / low-confidence items are never dropped solely on a date, consistent with the
        // eval philosophy. A null start date means no lower cutoff; a null end date falls back to the run's
        // start date. Future EFFECTIVE dates are legitimate horizon items and are deliberately NOT filtered.
        var (lowerBound, upperBound) = ScanDateRange.Resolve(context.Run);
        var outOfWindow = 0;

        var review = new Review
        {
            ThoughtProcess = decision.ThoughtProcess,
            LlmDecision = decision.Decision,
            FinalDecision = finalDecision,
            DecisionOverride = overridden,
            OverrideReason = overrideReason,
        };

        foreach (var verdict in decision.Items)
        {
            if (verdict.Index < 0 || verdict.Index >= documents.Count)
            {
                continue;
            }

            var document = documents[verdict.Index];
            var item = BuildResultItem(context, document, verdict, pass.Pass);

            if (verdict.Verdict == Verdict.NotRelevant)
            {
                // Discarded items are audit-only (never persisted to Cosmos), so we don't snapshot them.
                review.Discarded.Add(item);
            }
            else if (IsOutsideWindow(verdict, lowerBound, upperBound))
            {
                // Out-of-window: confidently published before the start date or after the end date.
                // Discard (audit-only) rather than surface content outside the scan's requested range.
                outOfWindow++;
                review.Discarded.Add(item);
            }
            else
            {
                // Carried item: snapshot exactly what the eval read to blob for audit/provenance.
                await PersistFullTextAsync(item, document, cancellationToken);
                review.Vetted.Add(item);
            }
        }

        pass.Review = review;

        _logger.LogInformation(
            "Loop controller: group '{GroupId}' pass {Pass}/{MaxLoops} -> {Decision} (eval said {LlmDecision}; {RelevantShare:P0} relevant; vetted {Vetted}, discarded {Discarded} (of which {OutOfWindow} outside window {Start}..{End}){Override}).",
            context.TopicGroup.Id, pass.Pass, context.TopicGroup.MaxLoops, finalDecision, decision.Decision,
            relevantShare, review.Vetted.Count, review.Discarded.Count, outOfWindow,
            lowerBound?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-",
            upperBound.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            overridden ? $"; override: {overrideReason}" : string.Empty);

        return finalDecision;
    }

    /// <summary>
    /// True when the item was confidently published outside the scan's date-range window - before
    /// <paramref name="lowerBound"/> (when set) or after <paramref name="upperBound"/>. Only a non-null
    /// <see cref="ItemVerdict.PublicationDate"/> with <see cref="DateConfidence.Medium"/> or
    /// <see cref="DateConfidence.High"/> confidence counts, so undated or low-confidence items are never
    /// dropped on a date alone.
    /// </summary>
    private static bool IsOutsideWindow(ItemVerdict verdict, DateOnly? lowerBound, DateOnly upperBound) =>
        verdict.PublicationDate is { } published
        && verdict.DateConfidence is DateConfidence.Medium or DateConfidence.High
        && (published > upperBound || (lowerBound is { } start && published < start));

    /// <summary>Share of the pass's evaluated items judged RELEVANT (0 when nothing was evaluated).</summary>
    private static double ComputeRelevantShare(IReadOnlyList<ItemVerdict> items)
    {
        if (items.Count == 0)
        {
            return 0d;
        }

        var relevant = items.Count(v => v.Verdict == Verdict.Relevant);
        return (double)relevant / items.Count;
    }

    private async Task PersistFullTextAsync(ResultItem item, FetchedDocument document, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(document.CleanedText))
        {
            // Nothing to snapshot (fetch failed with no snippet); leave the reference null.
            return;
        }

        item.FullTextBlobUri = await _fullTextStore.PersistAsync(
            item.RunId, item.GroupId, item.Id, document.CleanedText, cancellationToken);
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
            // Effective-date-aware fields extracted by the eval agent (Epic 6.1).
            PublicationDate = verdict.PublicationDate,
            EffectiveDate = verdict.EffectiveDate,
            AppliesFrom = verdict.AppliesFrom,
            AppliesTo = verdict.AppliesTo,
            DateConfidence = verdict.DateConfidence,
        };
    }
}
