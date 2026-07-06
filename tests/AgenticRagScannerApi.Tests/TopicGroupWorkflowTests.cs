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
    public async Task Workflow_WhenWebSearchFails_FinalizesAsFailed_AndSurfacesTheFailureOnThePass()
    {
        var context = WorkflowTestFactory.CreateContext(maxLoops: 1);

        // The search always fails (timeout/error), so no documents are retrieved: the group finalizes
        // (here at the maxLoops cap), but because the empty result was caused by a broken search the
        // group must be reported as Failed rather than a clean, completed empty scan.
        var (result, _) = await WorkflowTestFactory.RunToCompletionAsync(context, new FailingWebSearchAgent());

        result.Status.Should().Be("Failed");
        result.Items.Should().BeEmpty();
        result.LoopCount.Should().Be(1);

        // The failure is observable on the pass history (so it flows out to the caller/UI, not just logs).
        result.History!.Passes.Should().ContainSingle()
            .Which.SearchFailed.Should().BeTrue();
        result.History.Passes[0].SearchFailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Workflow_WhenWebSearchFailsEveryPass_RetriesToCap_ThenFinalizesAsFailed()
    {
        var context = WorkflowTestFactory.CreateContext(maxLoops: 3);

        // Every pass's search fails (transient error), so no documents are ever retrieved. Rather than
        // finalizing on the first broken pass, the loop must retry up to the cap before finalizing - and
        // because it carried nothing off failed searches, the group is reported as Failed.
        var (result, _) = await WorkflowTestFactory.RunToCompletionAsync(context, new FailingWebSearchAgent());

        result.Status.Should().Be("Failed");
        result.Items.Should().BeEmpty();
        result.LoopCount.Should().Be(3);
        result.History!.Passes.Should().HaveCount(3);
        result.History.Passes.Should().OnlyContain(p => p.SearchFailed);
    }

    [Fact]
    public async Task Workflow_Finalize_CarriesVettedItems_AndEnrichesThem()
    {
        var context = WorkflowTestFactory.CreateContext(maxLoops: 1, allowlist: ["https://www.gov.uk"]);

        var (result, _) = await WorkflowTestFactory.RunToCompletionAsync(context);

        result.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(i => i.Verdict == Verdict.Relevant || i.Verdict == Verdict.Borderline);
        result.Items.Should().OnlyContain(i => i.WhatItDoes != null);

        // Impact area and tags are group-level (story 8.2/8.3), set on the aggregate Deloitte View - not
        // per item. The Deloitte View is a single group-level aggregate (story 8.5).
        result.DeloitteView.Should().NotBeNull();
        result.DeloitteView!.DeloitteView.Should().NotBeNullOrWhiteSpace();
        result.DeloitteView.ImpactArea.Should().NotBeNullOrWhiteSpace();
        result.DeloitteView.Tags.Should().NotBeEmpty();
    }
}
