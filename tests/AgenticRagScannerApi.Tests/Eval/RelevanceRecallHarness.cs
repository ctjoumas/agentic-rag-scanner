using AgenticRagScannerApi.Core.Contracts;

namespace AgenticRagScannerApi.Tests.Eval;

/// <summary>The recall scores produced by <see cref="RelevanceRecallHarness"/> over a golden set.</summary>
internal sealed record RecallReport(
    int Total,
    int CarriedExpected,
    int CarriedRecalled,
    int RelevantExpected,
    int RelevantRecalled)
{
    /// <summary>
    /// Recall on the "carried" class (RELEVANT or BORDERLINE) - the compliance-critical metric, because a
    /// real update wrongly marked NOT_RELEVANT (a false negative) is the costly error. 1.0 when there is
    /// nothing to carry.
    /// </summary>
    public double CarriedRecall => CarriedExpected == 0 ? 1d : (double)CarriedRecalled / CarriedExpected;

    /// <summary>Stricter recall on the RELEVANT class only. 1.0 when there is nothing expected RELEVANT.</summary>
    public double RelevantRecall => RelevantExpected == 0 ? 1d : (double)RelevantRecalled / RelevantExpected;
}

/// <summary>
/// Pure recall harness for the relevance golden set (Epic 6, story 6.4). Given a predictor that maps a
/// document to a <see cref="Verdict"/>, it scores recall against the labeled golden items. Kept free of
/// any LLM dependency so the harness is deterministic and CI-safe; a deterministic oracle predictor is
/// used to validate the harness math, and the same harness can later be pointed at the real eval agent
/// (offline) to track recall over time.
/// </summary>
internal static class RelevanceRecallHarness
{
    /// <summary>"Carried" = the eval keeps the item (RELEVANT or BORDERLINE); NOT_RELEVANT is dropped.</summary>
    private static bool IsCarried(Verdict verdict) => verdict != Verdict.NotRelevant;

    public static RecallReport Evaluate(
        IReadOnlyList<GoldenItem> goldenSet,
        Func<GoldenItem, Verdict> predict)
    {
        var carriedExpected = 0;
        var carriedRecalled = 0;
        var relevantExpected = 0;
        var relevantRecalled = 0;

        foreach (var item in goldenSet)
        {
            var predicted = predict(item);

            if (IsCarried(item.Expected))
            {
                carriedExpected++;
                if (IsCarried(predicted))
                {
                    carriedRecalled++;
                }
            }

            if (item.Expected == Verdict.Relevant)
            {
                relevantExpected++;
                if (predicted == Verdict.Relevant)
                {
                    relevantRecalled++;
                }
            }
        }

        return new RecallReport(
            goldenSet.Count,
            carriedExpected,
            carriedRecalled,
            relevantExpected,
            relevantRecalled);
    }
}
