using System.Text.Json;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Pipeline;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows;

/// <summary>
/// The MAF executor for one topic group's agentic RAG loop. It is a thin adapter over
/// <see cref="TopicGroupPipeline"/>: each super-step runs exactly one pass; if the loop should
/// continue it sends itself <see cref="PassSignal.Continue"/> (a self-edge), otherwise it finalizes
/// the group and yields the <see cref="TopicGroupResult"/>. The in-memory <see cref="SearchHistory"/>
/// is checkpointed at every super-step so a run is resumable from any pass.
/// </summary>
[SendsMessage(typeof(PassSignal))]
[YieldsOutput(typeof(TopicGroupResult))]
public sealed class TopicGroupLoopExecutor : Executor<PassSignal>
{
    internal const string SearchHistoryStateKey = "SearchHistory";

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.General);

    private readonly TopicGroupContext _context;
    private readonly TopicGroupPipeline _pipeline;
    private readonly ILogger<TopicGroupLoopExecutor> _logger;

    public TopicGroupLoopExecutor(
        TopicGroupContext context,
        TopicGroupPipeline pipeline,
        ILogger<TopicGroupLoopExecutor> logger)
        : base($"topic-group-loop-{context.TopicGroup.Id}")
    {
        _context = context;
        _pipeline = pipeline;
        _logger = logger;
    }

    public override async ValueTask HandleAsync(PassSignal message, IWorkflowContext workflowContext, CancellationToken cancellationToken = default)
    {
        await _pipeline.RunPassAsync(_context, cancellationToken);

        if (_context.ShouldContinue())
        {
            _logger.LogInformation(
                "Topic group '{GroupId}': pass {Pass}/{MaxLoops} complete -> continue.",
                _context.TopicGroup.Id, _context.LoopCount, _context.TopicGroup.MaxLoops);

            await workflowContext.SendMessageAsync(PassSignal.Continue, cancellationToken: cancellationToken);
            return;
        }

        var items = await _pipeline.FinalizeAsync(_context, cancellationToken);

        var result = new TopicGroupResult
        {
            GroupId = _context.TopicGroup.Id,
            GroupName = _context.TopicGroup.Name,
            Status = "Completed",
            LoopCount = _context.LoopCount,
            Items = items,
        };

        _logger.LogInformation(
            "Topic group '{GroupId}': finalized after {Passes} pass(es) with {Items} item(s).",
            _context.TopicGroup.Id, _context.LoopCount, items.Count);

        await workflowContext.YieldOutputAsync(result, cancellationToken);
    }

    /// <summary>Persists the in-memory SearchHistory (as JSON) so the run can resume from this point.</summary>
    protected override ValueTask OnCheckpointingAsync(IWorkflowContext workflowContext, CancellationToken cancellationToken = default)
    {
        var snapshot = SearchHistorySerializer.ToSnapshot(_context.History);
        var json = JsonSerializer.Serialize(snapshot, s_jsonOptions);
        return workflowContext.QueueStateUpdateAsync(SearchHistoryStateKey, json, cancellationToken: cancellationToken);
    }

    /// <summary>Rebuilds the in-memory SearchHistory from the checkpoint when a run is resumed.</summary>
    protected override async ValueTask OnCheckpointRestoredAsync(IWorkflowContext workflowContext, CancellationToken cancellationToken = default)
    {
        var json = await workflowContext.ReadStateAsync<string>(SearchHistoryStateKey, cancellationToken: cancellationToken);
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        var snapshot = JsonSerializer.Deserialize<SearchHistorySnapshot>(json, s_jsonOptions);
        if (snapshot is not null)
        {
            SearchHistorySerializer.Restore(_context.History, snapshot);
        }
    }
}
