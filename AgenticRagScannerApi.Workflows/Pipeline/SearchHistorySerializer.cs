using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Pipeline;

/// <summary>
/// Maps <see cref="SearchHistory"/> to/from a <see cref="SearchHistorySnapshot"/> for MAF
/// checkpointing. <see cref="Restore"/> is idempotent: it clears the target collections and rebuilds
/// them from the snapshot, so restoring the same checkpoint twice yields the same state.
/// </summary>
public static class SearchHistorySerializer
{
    /// <summary>Captures the loop state as a serialization-friendly snapshot.</summary>
    public static SearchHistorySnapshot ToSnapshot(SearchHistory history) =>
        new(
            history.Passes.Select(ToPassSnapshot).ToList(),
            history.ProcessedKeys.ToList());

    /// <summary>Rebuilds <paramref name="target"/> in place from <paramref name="snapshot"/>.</summary>
    public static void Restore(SearchHistory target, SearchHistorySnapshot snapshot)
    {
        target.Passes.Clear();
        target.ProcessedKeys.Clear();

        foreach (var key in snapshot.ProcessedKeys)
        {
            target.ProcessedKeys.Add(key);
        }

        foreach (var passSnapshot in snapshot.Passes)
        {
            target.Passes.Add(ToPass(passSnapshot));
        }
    }

    private static LoopPassSnapshot ToPassSnapshot(LoopPass pass) =>
        new(
            pass.Pass,
            pass.Query,
            pass.QueryRationale,
            [.. pass.Hits],
            pass.Review is null ? null : ToReviewSnapshot(pass.Review));

    private static LoopPass ToPass(LoopPassSnapshot snapshot)
    {
        var pass = new LoopPass
        {
            Pass = snapshot.Pass,
            Query = snapshot.Query,
            QueryRationale = snapshot.QueryRationale,
        };

        pass.Hits.AddRange(snapshot.Hits);

        if (snapshot.Review is not null)
        {
            pass.Review = ToReview(snapshot.Review);
        }

        return pass;
    }

    private static ReviewSnapshot ToReviewSnapshot(Review review) =>
        new(
            review.ThoughtProcess,
            review.LlmDecision,
            review.FinalDecision,
            review.DecisionOverride,
            review.OverrideReason,
            [.. review.Vetted],
            [.. review.Discarded]);

    private static Review ToReview(ReviewSnapshot snapshot)
    {
        var review = new Review
        {
            ThoughtProcess = snapshot.ThoughtProcess,
            LlmDecision = snapshot.LlmDecision,
            FinalDecision = snapshot.FinalDecision,
            DecisionOverride = snapshot.DecisionOverride,
            OverrideReason = snapshot.OverrideReason,
        };

        review.Vetted.AddRange(snapshot.Vetted);
        review.Discarded.AddRange(snapshot.Discarded);

        return review;
    }
}
