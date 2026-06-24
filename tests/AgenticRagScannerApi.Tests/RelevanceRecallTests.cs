using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Tests.Eval;
using FluentAssertions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Story 6.4 - the eval-harness recall check runs over the relevance golden set. These tests validate
/// the harness math and the recall gate deterministically (no LLM): a sensible heuristic predictor must
/// recall every should-be-carried item (compliance prizes recall; false negatives are the costly error),
/// while a regressed predictor that drops everything must fail the gate so regressions are caught.
/// </summary>
public class RelevanceRecallTests
{
    private const double CarriedRecallGate = 1.0;

    [Fact]
    public void GoldenSet_HasCarriedAndDroppedExamples()
    {
        var set = RelevanceGoldenSet.Items;

        set.Should().Contain(i => i.Expected == Verdict.Relevant);
        set.Should().Contain(i => i.Expected == Verdict.Borderline);
        set.Should().Contain(i => i.Expected == Verdict.NotRelevant);
    }

    [Fact]
    public void HeuristicPredictor_MeetsCarriedRecallGate_OnGoldenSet()
    {
        var report = RelevanceRecallHarness.Evaluate(RelevanceGoldenSet.Items, HeuristicPredict);

        report.CarriedRecall.Should().BeGreaterThanOrEqualTo(CarriedRecallGate);
    }

    [Fact]
    public void RegressedPredictor_FailsCarriedRecallGate()
    {
        // A predictor that drops everything has zero recall on the carried class - the gate must catch it.
        var report = RelevanceRecallHarness.Evaluate(RelevanceGoldenSet.Items, _ => Verdict.NotRelevant);

        report.CarriedRecall.Should().BeLessThan(CarriedRecallGate);
        report.CarriedRecall.Should().Be(0d);
    }

    [Fact]
    public void PerfectOracle_ScoresFullRecall()
    {
        var report = RelevanceRecallHarness.Evaluate(RelevanceGoldenSet.Items, item => item.Expected);

        report.CarriedRecall.Should().Be(1d);
        report.RelevantRecall.Should().Be(1d);
    }

    /// <summary>
    /// A deterministic, recall-favoring stand-in for the eval agent: authoritative primary-source domains
    /// are RELEVANT; anything that still mentions the theme is carried as BORDERLINE; only clearly off-theme
    /// content is dropped. It is intentionally conservative (favoring recall) like the real eval rubric.
    /// </summary>
    private static Verdict HeuristicPredict(GoldenItem item)
    {
        var url = item.Url.ToLowerInvariant();
        var text = item.CleanedText.ToLowerInvariant();
        var mentionsTheme = ThemeTerms(item.Theme).Any(term => text.Contains(term));

        var isAuthoritative = url.Contains("gov.uk") || url.Contains("legislation.gov.uk");
        if (isAuthoritative && mentionsTheme)
        {
            return Verdict.Relevant;
        }

        return mentionsTheme ? Verdict.Borderline : Verdict.NotRelevant;
    }

    private static IEnumerable<string> ThemeTerms(string theme) => theme.ToLowerInvariant() switch
    {
        "advisory fuel rates" => ["advisory fuel rate", "fuel rate", "mileage", "company car"],
        "national insurance" => ["national insurance", "nic", "class 1"],
        _ => [theme.ToLowerInvariant()],
    };
}
