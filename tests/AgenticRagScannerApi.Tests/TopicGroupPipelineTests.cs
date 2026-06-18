using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using FluentAssertions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Story 2.2 - the composed loop runs in order, threads SearchHistory each pass, loops to maxLoops,
/// and the finalize chain enriches the carried items. Uses the real deterministic stubs (no mocks).
/// </summary>
public class TopicGroupPipelineTests
{
    [Fact]
    public async Task RunPassAsync_AppendsAPassEachLoop_AndStopsAtMaxLoops()
    {
        var context = WorkflowTestFactory.CreateContext(maxLoops: 3, allowlist: ["https://www.gov.uk"]);
        var pipeline = WorkflowTestFactory.CreatePipeline();

        var decisions = new List<LoopDecision>();
        var safety = 0;
        do
        {
            decisions.Add(await pipeline.RunPassAsync(context));
        }
        while (context.ShouldContinue() && ++safety < 10);

        context.LoopCount.Should().Be(3);
        context.History.Passes.Should().HaveCount(3);
        decisions.Should().HaveCount(3);
        decisions[^1].Should().Be(LoopDecision.Finalize);
    }

    [Fact]
    public async Task FinalizeAsync_CarriesVettedItems_AndEnrichesThem()
    {
        var context = WorkflowTestFactory.CreateContext(maxLoops: 1, allowlist: ["https://www.gov.uk"]);
        var pipeline = WorkflowTestFactory.CreatePipeline();

        await pipeline.RunPassAsync(context);
        var items = await pipeline.FinalizeAsync(context);

        items.Should().NotBeEmpty();
        items.Should().OnlyContain(i => i.Verdict == Verdict.Relevant || i.Verdict == Verdict.Borderline);
        items.Should().OnlyContain(i =>
            i.WhatItDoes != null && i.ImpactArea != null && i.Regulator != null &&
            i.Tags.Count > 0 && i.ImpactSummary != null);
    }
}
