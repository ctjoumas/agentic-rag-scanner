using System.Text.Json;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows;
using AgenticRagScannerApi.Workflows.Checkpointing;
using Microsoft.Agents.AI.Workflows;

namespace AgenticRagScannerApi.Orchestration;

/// <summary>
/// Epic 2 per-group executor: builds and runs the topic group's MAF workflow (the seven-executor
/// agentic RAG loop), checkpointing to Cosmos so a run is resumable, and returns its aggregated
/// <see cref="TopicGroupResult"/>. Replaces the Phase 1 <c>StubTopicGroupExecutor</c>. Logging is scoped
/// to <c>runId</c>/<c>topicGroupId</c>.
/// <para>
/// Outbound LLM/Bing calls funnel through the shared throttle at the point of each call
/// (<c>ResilientChatClient</c> / <c>WebSearchAgent</c>), not here: wrapping the whole workflow in the
/// throttle's concurrency limiter would hold a slot for the group's entire lifetime while its inner calls
/// wait for slots from the same limiter - a deadlock once groups run in parallel (Epic 13). Group-level
/// parallelism is capped by the orchestrator instead.
/// </para>
/// </summary>
public sealed class WorkflowTopicGroupExecutor : ITopicGroupExecutor
{
    private static readonly JsonSerializerOptions s_checkpointOptions = new(JsonSerializerDefaults.General);

    private readonly IServiceProvider _serviceProvider;
    private readonly CosmosCheckpointStore _checkpointStore;
    private readonly ILogger<WorkflowTopicGroupExecutor> _logger;

    public WorkflowTopicGroupExecutor(
        IServiceProvider serviceProvider,
        CosmosCheckpointStore checkpointStore,
        ILogger<WorkflowTopicGroupExecutor> logger)
    {
        _serviceProvider = serviceProvider;
        _checkpointStore = checkpointStore;
        _logger = logger;
    }

    public async Task<TopicGroupResult> ExecuteAsync(TopicGroupContext context, CancellationToken cancellationToken = default)
    {
        var group = context.TopicGroup;

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["runId"] = context.Run.RunId,
            ["topicGroupId"] = group.Id,
        });

        _logger.LogInformation("Topic group '{TopicGroupName}' workflow starting.", group.Name);

        var result = await RunWorkflowAsync(context, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Topic group '{TopicGroupName}' workflow completed: {LoopCount} pass(es), {ItemCount} item(s).",
            group.Name, result.LoopCount, result.Items.Count);

        return result;
    }

    private async Task<TopicGroupResult> RunWorkflowAsync(TopicGroupContext context, CancellationToken cancellationToken)
    {
        var workflow = TopicGroupWorkflow.Build(context, _serviceProvider);
        var checkpointManager = CheckpointManager.CreateJson(_checkpointStore, s_checkpointOptions);

        var run = await InProcessExecution
            .RunStreamingAsync(workflow, TopicGroupWorkflow.StartSignal, checkpointManager)
            .ConfigureAwait(false);

        TopicGroupResult? result = null;

        try
        {
            await foreach (var workflowEvent in run.WatchStreamAsync().WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                switch (workflowEvent)
                {
                    case WorkflowOutputEvent output when output.Data is TopicGroupResult topicGroupResult:
                        result = topicGroupResult;
                        break;

                    case SuperStepCompletedEvent superStep when superStep.CompletionInfo?.Checkpoint is { } checkpoint:
                        _logger.LogDebug(
                            "Checkpoint persisted for group '{GroupId}': {CheckpointId}.",
                            context.TopicGroup.Id, checkpoint.CheckpointId);
                        break;

                    case WorkflowErrorEvent error:
                        _logger.LogError(error.Exception, "Topic group '{GroupId}' workflow error.", context.TopicGroup.Id);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation is not a failure: checkpoints persisted so far remain valid, so the run can be
            // resumed later. Surface it as cancellation rather than the "no result" error below.
            _logger.LogInformation("Topic group '{GroupId}' workflow canceled; checkpoints preserved for resume.", context.TopicGroup.Id);
            throw;
        }

        return result ?? throw new InvalidOperationException(
            $"Topic group '{context.TopicGroup.Id}' workflow completed without producing a result.");
    }
}
