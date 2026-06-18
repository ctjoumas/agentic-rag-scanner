using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Orchestration;

/// <summary>
/// Runs a single topic group to completion within a scan run and returns its aggregated result.
/// Phase 1 ships a stub implementation that returns a placeholder; Epic 2 replaces it with the real
/// MAF workflow. The orchestrator invokes one executor per group, sequentially.
/// </summary>
public interface ITopicGroupExecutor
{
    /// <summary>
    /// Executes the per-group pipeline for <paramref name="context"/> and returns its result.
    /// </summary>
    Task<TopicGroupResult> ExecuteAsync(TopicGroupContext context, CancellationToken cancellationToken = default);
}