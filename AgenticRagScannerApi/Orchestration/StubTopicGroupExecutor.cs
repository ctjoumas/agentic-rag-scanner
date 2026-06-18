using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Core.Throttling;

namespace AgenticRagScannerApi.Orchestration;

/// <summary>
/// Phase 1 walking-skeleton executor: returns a placeholder result without performing real work.
/// It still exercises the orchestration seams - the shared throttle (the gate Epic 2's outbound
/// LLM/Bing calls will funnel through) and per-group structured logging keyed by runId/topicGroupId.
/// Epic 2 replaces this with the real MAF workflow.
/// </summary>
public sealed class StubTopicGroupExecutor : ITopicGroupExecutor
{
    private readonly ISharedThrottle _throttle;
    private readonly ILogger<StubTopicGroupExecutor> _logger;

    public StubTopicGroupExecutor(ISharedThrottle throttle, ILogger<StubTopicGroupExecutor> logger)
    {
        _throttle = throttle;
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

        _logger.LogInformation("Topic group '{TopicGroupName}' starting.", group.Name);

        // Wire the shared throttle so the rate-limiting seam is in place for Epic 2's outbound
        // LLM/Bing calls. With the Phase 0 NoOpThrottle this is a pass-through and the stub does
        // no real work yet.
        var result = await _throttle.ExecuteAsync(
            _ => Task.FromResult(new TopicGroupResult
            {
                GroupId = group.Id,
                GroupName = group.Name,
                Status = "Completed",
                LoopCount = context.LoopCount,
                Items = [],
            }),
            permits: 1,
            cancellationToken);

        _logger.LogInformation(
            "Topic group '{TopicGroupName}' completed with {ItemCount} item(s).",
            group.Name, result.Items.Count);

        return result;
    }
}