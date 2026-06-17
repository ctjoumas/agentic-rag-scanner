using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using FluentAssertions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Locks in the pass-centric agentic-loop state: each <see cref="LoopPass"/> is the unit of record,
/// and the flat views on <see cref="SearchHistory"/> are projections over it (so they cannot drift).
/// Mirrors the two-pass walkthrough used during design.
/// </summary>
public class TopicGroupContextTests
{
    private const string RunId = "run-1";
    private const string GroupId = "tg-afr";

    private static TopicGroupContext NewContext(int maxLoops = 3) => new()
    {
        Run = new RunContext
        {
            RunId = RunId,
            Jurisdiction = "United Kingdom",
            AuthoritativeSources = ["legislation.gov.uk", "gov.uk/hm-revenue-customs", "supremecourt.uk"],
        },
        TopicGroup = new TopicGroup
        {
            Id = GroupId,
            Name = "Advisory Fuel Rates",
            Keywords = ["Advisory Fuel Rates", "AFR", "Company Car", "EV"],
            MaxLoops = maxLoops,
        },
    };

    private static ResultItem Item(string id, Verdict verdict, int pass) => new()
    {
        RunId = RunId,
        GroupId = GroupId,
        Id = id,
        SourceUrls = [$"https://gov.uk/{id}"],
        Verdict = verdict,
        FoundOnPass = pass,
    };

    [Fact]
    public void History_AfterTwoPasses_ShouldProjectQueriesReviewsDecisionsAndResults()
    {
        var ctx = NewContext();

        // Pass 1 -> Retry: 2 vetted (Relevant, Borderline), 1 discarded (NotRelevant)
        var pass1 = new LoopPass { Pass = 1, Query = "AFR 2026 site:gov.uk/hmrc" };
        ctx.History.Passes.Add(pass1);
        var review1 = new Review
        {
            ThoughtProcess = "2/3 on-topic; EV/Company-Car synonyms untested -> retry",
            LlmDecision = LoopDecision.Retry,
            FinalDecision = LoopDecision.Retry,
        };
        review1.Vetted.Add(Item("A", Verdict.Relevant, 1));
        review1.Vetted.Add(Item("B", Verdict.Borderline, 1));
        review1.Discarded.Add(Item("C", Verdict.NotRelevant, 1));
        pass1.Review = review1;

        // Pass 2 -> Finalize: 1 vetted (Relevant), 1 discarded (NotRelevant)
        var pass2 = new LoopPass { Pass = 2, Query = "Company Car EV AFR 2026 site:gov.uk/hmrc" };
        ctx.History.Passes.Add(pass2);
        var review2 = new Review
        {
            ThoughtProcess = "base + EV now covered; goal met -> finalize",
            LlmDecision = LoopDecision.Finalize,
            FinalDecision = LoopDecision.Finalize,
        };
        review2.Vetted.Add(Item("D", Verdict.Relevant, 2));
        review2.Discarded.Add(Item("E", Verdict.NotRelevant, 2));
        pass2.Review = review2;

        ctx.LoopCount.Should().Be(2);
        ctx.History.CurrentPass.Should().BeSameAs(pass2);
        ctx.History.Queries.Should().Equal(pass1.Query, pass2.Query);
        ctx.History.Reviews.Should().Equal(review1.ThoughtProcess, review2.ThoughtProcess);
        ctx.History.Decisions.Should().Equal(LoopDecision.Retry, LoopDecision.Finalize);
        ctx.History.Vetted.Select(i => i.Id).Should().Equal("A", "B", "D");
        ctx.History.Discarded.Select(i => i.Id).Should().Equal("C", "E");
    }

    [Fact]
    public void ShouldContinue_AcrossPasses_ShouldFlipFromRetryToFinalize()
    {
        var ctx = NewContext();

        // Before any pass: start the first one.
        ctx.ShouldContinue().Should().BeTrue();

        var pass1 = new LoopPass { Pass = 1, Query = "q1" };
        ctx.History.Passes.Add(pass1);
        pass1.Review = new Review
        {
            ThoughtProcess = "gaps remain -> retry",
            LlmDecision = LoopDecision.Retry,
            FinalDecision = LoopDecision.Retry,
        };
        ctx.ShouldContinue().Should().BeTrue();

        var pass2 = new LoopPass { Pass = 2, Query = "q2" };
        ctx.History.Passes.Add(pass2);
        pass2.Review = new Review
        {
            ThoughtProcess = "covered -> finalize",
            LlmDecision = LoopDecision.Finalize,
            FinalDecision = LoopDecision.Finalize,
        };
        ctx.ShouldContinue().Should().BeFalse();
    }

    [Fact]
    public void ShouldContinue_AtMaxLoops_ShouldReturnFalseEvenWhenRetry()
    {
        var ctx = NewContext(maxLoops: 2);

        for (var p = 1; p <= 2; p++)
        {
            var pass = new LoopPass { Pass = p, Query = $"q{p}" };
            ctx.History.Passes.Add(pass);
            pass.Review = new Review
            {
                ThoughtProcess = "still gaps -> retry",
                LlmDecision = LoopDecision.Retry,
                FinalDecision = LoopDecision.Retry,
            };
        }

        ctx.LoopCount.Should().Be(2);
        ctx.ShouldContinue().Should().BeFalse(); // hard cap reached despite Retry
    }
}
