using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows;
using FluentAssertions;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Story 2.1 - proves "a run is resumable from checkpoint" using the documented MAF C# pattern:
/// a CheckpointInfo is taken from <see cref="StreamingRun.Checkpoints"/> and a fresh workflow is
/// rehydrated from it via <see cref="InProcessExecution.ResumeStreamingAsync"/>. Uses the in-memory
/// checkpoint manager (same ICheckpointManager contract the Cosmos store implements) so no external
/// dependency is needed.
/// </summary>
public class WorkflowResumeTests
{
    [Fact]
    public async Task ResumeStreamingAsync_RehydratesSearchHistory_AndCompletes()
    {
        var checkpointManager = CheckpointManager.CreateInMemory();

        // First run: execute to completion so the manager has a checkpoint per super-step.
        var firstContext = WorkflowTestFactory.CreateContext(maxLoops: 3, allowlist: ["https://www.gov.uk"]);
        var firstWorkflow = TopicGroupWorkflow.Build(firstContext, WorkflowTestFactory.CreatePipeline(), NullLoggerFactory.Instance);
        var firstRun = await InProcessExecution.RunStreamingAsync(
            firstWorkflow, TopicGroupWorkflow.StartSignal, checkpointManager);

        await foreach (var _ in firstRun.WatchStreamAsync()) { /* drain to completion */ }

        // Take an early checkpoint (the first one) to resume from - i.e. partway through the loop.
        firstRun.Checkpoints.Should().NotBeEmpty("each super-step creates a checkpoint");
        var resumeFrom = firstRun.Checkpoints[0];

        // Rehydrate a brand-new workflow (fresh, empty SearchHistory) from that checkpoint.
        var resumedContext = WorkflowTestFactory.CreateContext(maxLoops: 3, allowlist: ["https://www.gov.uk"]);
        resumedContext.History.Passes.Should().BeEmpty("the resumed context starts empty before restore");

        var resumedWorkflow = TopicGroupWorkflow.Build(resumedContext, WorkflowTestFactory.CreatePipeline(), NullLoggerFactory.Instance);
        var resumedRun = await InProcessExecution.ResumeStreamingAsync(
            resumedWorkflow, resumeFrom, checkpointManager);

        TopicGroupResult? result = null;
        await foreach (var workflowEvent in resumedRun.WatchStreamAsync())
        {
            if (workflowEvent is WorkflowOutputEvent { Data: TopicGroupResult topicGroupResult })
            {
                result = topicGroupResult;
            }
        }

        // The executor's OnCheckpointRestoredAsync rehydrated SearchHistory from the checkpoint, and the
        // resumed run continued to completion (rather than starting a fresh loop at pass 1).
        resumedContext.History.Passes.Should().NotBeEmpty("OnCheckpointRestoredAsync should rehydrate SearchHistory");
        result.Should().NotBeNull("the resumed run should continue to completion and yield a result");
        result!.LoopCount.Should().Be(3);
        result.Status.Should().Be("Completed");
    }
}
