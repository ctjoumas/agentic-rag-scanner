using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows;
using FluentAssertions;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging.Abstractions;

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
        var pipeline = WorkflowTestFactory.CreatePipeline();
        var workflow = TopicGroupWorkflow.Build(context, pipeline, NullLoggerFactory.Instance);
        var checkpointManager = CheckpointManager.CreateInMemory();

        var run = await InProcessExecution.RunStreamingAsync(workflow, TopicGroupWorkflow.StartSignal, checkpointManager);

        TopicGroupResult? result = null;
        var checkpoints = 0;
        await foreach (var workflowEvent in run.WatchStreamAsync())
        {
            switch (workflowEvent)
            {
                case WorkflowOutputEvent output when output.Data is TopicGroupResult topicGroupResult:
                    result = topicGroupResult;
                    break;

                case SuperStepCompletedEvent superStep when superStep.CompletionInfo?.Checkpoint is not null:
                    checkpoints++;
                    break;
            }
        }

        result.Should().NotBeNull();
        result!.LoopCount.Should().Be(2);
        result.Status.Should().Be("Completed");
        result.Items.Should().NotBeEmpty();
        checkpoints.Should().BeGreaterThan(0);
    }
}
