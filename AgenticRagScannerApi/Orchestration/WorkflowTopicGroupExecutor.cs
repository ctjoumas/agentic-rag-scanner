using System.Text.Json;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Core.Throttling;
using AgenticRagScannerApi.Workflows;
using AgenticRagScannerApi.Workflows.Checkpointing;
using AgenticRagScannerApi.Workflows.Pipeline;
using Microsoft.Agents.AI.Workflows;

namespace AgenticRagScannerApi.Orchestration;

/// <summary>
/// Epic 2 per-group executor: builds and runs the topic group's MAF workflow (one self-looping pass
/// per super-step), checkpointing to Cosmos so a run is resumable, and returns its aggregated
/// <see cref="TopicGroupResult"/>. Replaces the Phase 1 <c>StubTopicGroupExecutor</c>. Outbound work
/// funnels through the shared throttle (the seam Epic 3+ real LLM/Bing calls use), and logging is
/// scoped to <c>runId</c>/<c>topicGroupId</c>.
/// </summary>
public sealed class WorkflowTopicGroupExecutor : ITopicGroupExecutor
{
    private static readonly JsonSerializerOptions s_checkpointOptions = new(JsonSerializerDefaults.General);

    private readonly TopicGroupPipeline _pipeline;
    private readonly CosmosCheckpointStore _checkpointStore;
    private readonly ISharedThrottle _throttle;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<WorkflowTopicGroupExecutor> _logger;

    public WorkflowTopicGroupExecutor(
        TopicGroupPipeline pipeline,
        CosmosCheckpointStore checkpointStore,
        ISharedThrottle throttle,
        ILoggerFactory loggerFactory,
        ILogger<WorkflowTopicGroupExecutor> logger)
    {
        _pipeline = pipeline;
        _checkpointStore = checkpointStore;
        _throttle = throttle;
        _loggerFactory = loggerFactory;
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

        // Outbound (LLM/Bing) calls inside the workflow funnel through the shared throttle. With the
        // Phase 0 NoOpThrottle this is a pass-through; real TPM/RPM/QPS limits arrive later.
        var result = await _throttle.ExecuteAsync(
            ct => RunWorkflowAsync(context, ct),
            permits: 1,
            cancellationToken);

        _logger.LogInformation(
            "Topic group '{TopicGroupName}' workflow completed: {LoopCount} pass(es), {ItemCount} item(s).",
            group.Name, result.LoopCount, result.Items.Count);

        return result;
    }

    private async Task<TopicGroupResult> RunWorkflowAsync(TopicGroupContext context, CancellationToken cancellationToken)
    {
        var workflow = TopicGroupWorkflow.Build(context, _pipeline, _loggerFactory);
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
