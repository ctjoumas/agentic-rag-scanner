using System.Text.Json;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Pipeline;
using Microsoft.Agents.AI.Workflows;

namespace AgenticRagScannerApi.Workflows.Executors;

/// <summary>
/// Persists and rehydrates the per-group <see cref="TopicGroupContext.History"/> across MAF
/// checkpoints. In the seven-executor decomposition every executor shares the same injected
/// <see cref="TopicGroupContext"/>, so the loop's accumulating state lives on one object that no
/// single executor owns. This helper writes that state to a <em>named</em> ("shared") scope rather
/// than an executor's private scope, so any executor can read it back on resume - the framework's
/// <see cref="ScopeKey"/> treats a named scope as shared across executors.
/// </summary>
/// <remarks>
/// The MAF state store is cumulative: a value written by one executor in an early super-step is still
/// present in a checkpoint taken after a later step. That is what makes per-super-step resume work -
/// a crash after step 4 resumes with the pass and its hits already in <see cref="SearchHistory"/>,
/// without replaying steps 1-3.
/// </remarks>
internal static class SearchHistoryCheckpoint
{
    /// <summary>The state key the loop history is stored under (within the shared scope).</summary>
    private const string StateKey = "SearchHistory";

    /// <summary>A named scope shared across all of the group's executors (null would be private).</summary>
    private const string ScopeName = "shared";

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.General);

    /// <summary>Serializes the current <see cref="SearchHistory"/> into the shared checkpoint scope.</summary>
    public static ValueTask SaveAsync(IWorkflowContext workflowContext, TopicGroupContext context, CancellationToken cancellationToken)
    {
        var snapshot = SearchHistorySerializer.ToSnapshot(context.History);
        var json = JsonSerializer.Serialize(snapshot, s_jsonOptions);
        return workflowContext.QueueStateUpdateAsync(StateKey, json, ScopeName, cancellationToken);
    }

    /// <summary>
    /// Rebuilds the in-memory <see cref="SearchHistory"/> from the shared scope when a run is resumed.
    /// Idempotent: it only restores when the in-memory history is still empty, so it is safe to call
    /// from every executor's restore hook.
    /// </summary>
    public static async ValueTask RestoreAsync(IWorkflowContext workflowContext, TopicGroupContext context, CancellationToken cancellationToken)
    {
        if (context.History.Passes.Count > 0)
        {
            return;
        }

        var json = await workflowContext.ReadStateAsync<string>(StateKey, ScopeName, cancellationToken);
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        var snapshot = JsonSerializer.Deserialize<SearchHistorySnapshot>(json, s_jsonOptions);
        if (snapshot is not null)
        {
            SearchHistorySerializer.Restore(context.History, snapshot);
        }
    }
}
