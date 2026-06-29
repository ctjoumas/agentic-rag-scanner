using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using FluentAssertions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Epic 2 demo (stories 2.1/2.2) - the real MAF workflow runs end-to-end on the stub data: it loops
/// to maxLoops, yields an aggregated <see cref="TopicGroupResult"/>, and creates a checkpoint each
/// super-step. Uses the in-memory checkpoint manager (same ICheckpointManager contract as Cosmos) so
/// the test needs no external dependency.
/// </summary>
public class TopicGroupWorkflowTests
{
    [Fact]
    public async Task Workflow_RunsToMaxLoops_YieldsResult_AndCreatesCheckpoints()
    {
        var context = WorkflowTestFactory.CreateContext(maxLoops: 2, allowlist: ["https://www.gov.uk"]);

        var (result, checkpoints) = await WorkflowTestFactory.RunToCompletionAsync(context);

        result.LoopCount.Should().Be(2);
        result.Status.Should().Be("Completed");
        result.Items.Should().NotBeEmpty();
        checkpoints.Should().BeGreaterThan(0);

        // The full per-pass history is surfaced on the result (and so flows out through the API to a
        // future developer UI): one recorded pass per loop, each with its query and review.
        result.History.Should().NotBeNull();
        result.History!.Passes.Should().HaveCount(result.LoopCount);
        result.History.Passes.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.Query) && p.Review != null);
    }

    [Fact]
    public async Task Workflow_LoopsToMaxLoops_AndFinalizesOnTheLastPass()
    {
        var context = WorkflowTestFactory.CreateContext(maxLoops: 3, allowlist: ["https://www.gov.uk"]);

        var (result, _) = await WorkflowTestFactory.RunToCompletionAsync(context);

        // A pass is appended each loop until the maxLoops cap, and the controller finalizes the last one.
        result.LoopCount.Should().Be(3);
        result.History!.Passes.Should().HaveCount(3);
        result.History.Passes[^1].Review!.FinalDecision.Should().Be(LoopDecision.Finalize);
    }

    [Fact]
    public async Task Workflow_Finalize_CarriesVettedItems_AndEnrichesThem()
    {
        var context = WorkflowTestFactory.CreateContext(maxLoops: 1, allowlist: ["https://www.gov.uk"]);

        var (result, _) = await WorkflowTestFactory.RunToCompletionAsync(context);

        result.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(i => i.Verdict == Verdict.Relevant || i.Verdict == Verdict.Borderline);
        result.Items.Should().OnlyContain(i =>
            i.WhatItDoes != null && i.ImpactArea != null && i.Regulator != null &&
            i.Tags.Count > 0 && i.ImpactSummary != null);
    }
}
