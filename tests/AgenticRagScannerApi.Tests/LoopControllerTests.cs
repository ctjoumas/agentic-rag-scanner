using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Pipeline;
using AgenticRagScannerApi.Workflows.Steps;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Story 2.3 / Epic 6.2 - the loop controller honors per-group maxLoops, maps eval verdicts into the
/// pass review (vetted = Relevant/Borderline, discarded = NotRelevant), and applies the recall override
/// (eval wants to finalize but the pass was mostly RELEVANT -> retry for more, subject to the cap).
/// </summary>
public class LoopControllerTests
{
    [Fact]
    public async Task ReviewPass_UnderCap_ReturnsRetry_AndMapsVerdicts()
    {
        var context = ArrangeWithCurrentPass(maxLoops: 3, priorPasses: 0);
        var controller = new LoopController(new StubFullTextStore(), NullLogger<LoopController>.Instance);
        var documents = new List<FetchedDocument>
        {
            WorkflowTestFactory.Doc("https://gov.uk/a"),
            WorkflowTestFactory.Doc("https://gov.uk/b"),
            WorkflowTestFactory.Doc("https://gov.uk/c"),
        };
        var decision = WorkflowTestFactory.Decision(Verdict.Relevant, Verdict.Borderline, Verdict.NotRelevant);

        var result = await controller.ReviewPassAsync(context, documents, decision);

        result.Should().Be(LoopDecision.Retry);
        var review = context.History.CurrentPass!.Review!;
        review.Vetted.Should().HaveCount(2);
        review.Discarded.Should().HaveCount(1);
    }

    [Fact]
    public async Task ReviewPass_AtCap_ReturnsFinalize_AndFlagsOverride()
    {
        var context = ArrangeWithCurrentPass(maxLoops: 3, priorPasses: 2);
        var controller = new LoopController(new StubFullTextStore(), NullLogger<LoopController>.Instance);
        var documents = new List<FetchedDocument> { WorkflowTestFactory.Doc("https://gov.uk/a") };
        var decision = WorkflowTestFactory.Decision(Verdict.Relevant);

        var result = await controller.ReviewPassAsync(context, documents, decision);

        result.Should().Be(LoopDecision.Finalize);
        context.History.CurrentPass!.Review!.DecisionOverride.Should().BeTrue();
    }

    [Fact]
    public async Task ReviewPass_UnderCap_EvalFinalize_IsHonored_WhenNotMostlyRelevant()
    {
        var context = ArrangeWithCurrentPass(maxLoops: 3, priorPasses: 0);
        var controller = new LoopController(new StubFullTextStore(), NullLogger<LoopController>.Instance);
        var documents = new List<FetchedDocument> { WorkflowTestFactory.Doc("https://gov.uk/a") };
        var decision = WorkflowTestFactory.DecisionWith(LoopDecision.Finalize, Verdict.Borderline);

        var result = await controller.ReviewPassAsync(context, documents, decision);

        // Eval wants to finalize and the pass is not RELEVANT-rich, so the recall override stays quiet.
        result.Should().Be(LoopDecision.Finalize);
        context.History.CurrentPass!.Review!.DecisionOverride.Should().BeFalse();
    }

    [Fact]
    public async Task ReviewPass_UnderCap_RecallOverride_RetriesWhenMostlyRelevant()
    {
        var context = ArrangeWithCurrentPass(maxLoops: 5, priorPasses: 0);
        var controller = new LoopController(new StubFullTextStore(), NullLogger<LoopController>.Instance);
        var documents = new List<FetchedDocument>
        {
            WorkflowTestFactory.Doc("https://gov.uk/a"),
            WorkflowTestFactory.Doc("https://gov.uk/b"),
            WorkflowTestFactory.Doc("https://gov.uk/c"),
            WorkflowTestFactory.Doc("https://gov.uk/d"),
            WorkflowTestFactory.Doc("https://gov.uk/e"),
        };
        // 5/5 relevant (> 80%); the eval wanted to finalize, but a vein this rich likely has more to find,
        // so the recall override flips it to another retry.
        var decision = WorkflowTestFactory.DecisionWith(
            LoopDecision.Finalize,
            Verdict.Relevant, Verdict.Relevant, Verdict.Relevant, Verdict.Relevant, Verdict.Relevant);

        var result = await controller.ReviewPassAsync(context, documents, decision);

        result.Should().Be(LoopDecision.Retry);
        var review = context.History.CurrentPass!.Review!;
        review.DecisionOverride.Should().BeTrue();
        review.OverrideReason.Should().Contain("recall override");
    }

    [Fact]
    public async Task ReviewPass_AtCap_FinalizesEvenWhenMostlyRelevant()
    {
        var context = ArrangeWithCurrentPass(maxLoops: 2, priorPasses: 1);
        var controller = new LoopController(new StubFullTextStore(), NullLogger<LoopController>.Instance);
        var documents = new List<FetchedDocument>
        {
            WorkflowTestFactory.Doc("https://gov.uk/a"),
            WorkflowTestFactory.Doc("https://gov.uk/b"),
            WorkflowTestFactory.Doc("https://gov.uk/c"),
            WorkflowTestFactory.Doc("https://gov.uk/d"),
            WorkflowTestFactory.Doc("https://gov.uk/e"),
        };
        // 5/5 relevant would trip the recall override, but the cap is the hard stop and wins.
        var decision = WorkflowTestFactory.DecisionWith(
            LoopDecision.Finalize,
            Verdict.Relevant, Verdict.Relevant, Verdict.Relevant, Verdict.Relevant, Verdict.Relevant);

        var result = await controller.ReviewPassAsync(context, documents, decision);

        result.Should().Be(LoopDecision.Finalize);
        context.History.CurrentPass!.Review!.DecisionOverride.Should().BeFalse();
    }

    [Fact]
    public async Task ReviewPass_UnderCap_EvalRetry_IsHonored_EvenWhenMostlyRelevant()
    {
        var context = ArrangeWithCurrentPass(maxLoops: 5, priorPasses: 0);
        var controller = new LoopController(new StubFullTextStore(), NullLogger<LoopController>.Instance);
        var documents = new List<FetchedDocument>
        {
            WorkflowTestFactory.Doc("https://gov.uk/a"),
            WorkflowTestFactory.Doc("https://gov.uk/b"),
            WorkflowTestFactory.Doc("https://gov.uk/c"),
        };
        // The override only ever turns a Finalize into a Retry; an eval Retry is honored as-is, no override.
        var decision = WorkflowTestFactory.DecisionWith(
            LoopDecision.Retry, Verdict.Relevant, Verdict.Relevant, Verdict.Borderline);

        var result = await controller.ReviewPassAsync(context, documents, decision);

        result.Should().Be(LoopDecision.Retry);
        context.History.CurrentPass!.Review!.DecisionOverride.Should().BeFalse();
    }

    [Fact]
    public async Task ReviewPass_SnapshotsCarriedFullText_ButNotDiscarded()
    {
        var context = ArrangeWithCurrentPass(maxLoops: 3, priorPasses: 0);
        var store = new StubFullTextStore();
        var controller = new LoopController(store, NullLogger<LoopController>.Instance);
        var documents = new List<FetchedDocument>
        {
            WorkflowTestFactory.Doc("https://gov.uk/a"),
            WorkflowTestFactory.Doc("https://gov.uk/b"),
            WorkflowTestFactory.Doc("https://gov.uk/c"),
        };
        var decision = WorkflowTestFactory.Decision(Verdict.Relevant, Verdict.Borderline, Verdict.NotRelevant);

        await controller.ReviewPassAsync(context, documents, decision);

        // Only the two carried (Relevant/Borderline) items are snapshotted; the discarded one is not.
        store.Persisted.Should().HaveCount(2);
        var review = context.History.CurrentPass!.Review!;
        review.Vetted.Should().OnlyContain(item => item.FullTextBlobUri != null);
        review.Discarded.Should().OnlyContain(item => item.FullTextBlobUri == null);
    }

    [Fact]
    public async Task ReviewPass_NoCleanedText_LeavesBlobReferenceNull()
    {
        var context = ArrangeWithCurrentPass(maxLoops: 3, priorPasses: 0);
        var store = new StubFullTextStore();
        var controller = new LoopController(store, NullLogger<LoopController>.Instance);
        var documents = new List<FetchedDocument>
        {
            new() { Hit = new SearchHit { Url = "https://gov.uk/a", SourceQuery = "q" }, CleanedText = null },
        };
        var decision = WorkflowTestFactory.Decision(Verdict.Relevant);

        await controller.ReviewPassAsync(context, documents, decision);

        store.Persisted.Should().BeEmpty();
        context.History.CurrentPass!.Review!.Vetted.Should().OnlyContain(item => item.FullTextBlobUri == null);
    }

    private static TopicGroupContext ArrangeWithCurrentPass(int maxLoops, int priorPasses)
    {
        var context = WorkflowTestFactory.CreateContext(maxLoops: maxLoops);

        for (var i = 0; i < priorPasses; i++)
        {
            context.History.Passes.Add(new LoopPass { Pass = i + 1, Query = $"q{i}" });
        }

        context.History.Passes.Add(new LoopPass { Pass = priorPasses + 1, Query = "current" });
        return context;
    }
}
