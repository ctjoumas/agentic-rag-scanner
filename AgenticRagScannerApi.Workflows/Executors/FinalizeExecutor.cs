using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Agents;
using AgenticRagScannerApi.Workflows.Pipeline;
using AgenticRagScannerApi.Workflows.Steps;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Executors;

/// <summary>
/// Step 7 of the seven-executor decomposition: the loop's terminal tail. Reached only via the
/// <see cref="LoopControllerExecutor"/> <c>Finalize</c> conditional edge. Routes the vetted verdicts
/// (<see cref="IVerdictRouting"/>) then runs the enrichment chain
/// (Enrichment → Categorize → Summarize&amp;Impact) over the carried items, and yields the aggregated
/// <see cref="TopicGroupResult"/> as the workflow output.
/// </summary>
/// <remarks>
/// Single input (<see cref="Review"/> - its arrival is the "loop is done" signal; its fields are not
/// read here because <see cref="LoopControllerExecutor"/> already persisted the pass Review). As a
/// terminal node with nothing downstream it emits no edge message; it uses the non-generic
/// <see cref="Executor{TInput}"/> shortcut and surfaces the result via
/// <see cref="IWorkflowContext.YieldOutputAsync"/>, matching the monolith's pattern.
/// </remarks>
[YieldsOutput(typeof(TopicGroupResult))]
public sealed class FinalizeExecutor : Executor<Review>
{
    private readonly TopicGroupContext _context;
    private readonly IVerdictRouting _verdictRouting;
    private readonly IEnrichmentAgent _enrichment;
    private readonly ICategorizeAgent _categorize;
    private readonly ISummarizeImpactAgent _summarize;
    private readonly ILogger<FinalizeExecutor> _logger;

    public FinalizeExecutor(
        TopicGroupContext context,
        IVerdictRouting verdictRouting,
        IEnrichmentAgent enrichment,
        ICategorizeAgent categorize,
        ISummarizeImpactAgent summarize,
        ILogger<FinalizeExecutor> logger)
        : base($"finalize-{context.TopicGroup.Id}")
    {
        _context = context;
        _verdictRouting = verdictRouting;
        _enrichment = enrichment;
        _categorize = categorize;
        _summarize = summarize;
        _logger = logger;
    }

    /// <summary>
    /// Routes verdicts, runs the enrichment chain over the carried items, and yields the group result.
    /// </summary>
    public override async ValueTask HandleAsync(Review message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var carried = _verdictRouting.Route(_context);

        foreach (var item in carried)
        {
            await _enrichment.EnrichAsync(item, _context, cancellationToken);
            await _categorize.CategorizeAsync(item, cancellationToken);
            await _summarize.SummarizeAsync(item, _context, cancellationToken);
        }

        var result = new TopicGroupResult
        {
            GroupId = _context.TopicGroup.Id,
            GroupName = _context.TopicGroup.Name,
            Status = "Completed",
            LoopCount = _context.LoopCount,
            Items = carried,
            History = SearchHistorySerializer.ToSnapshot(_context.History),
        };

        _logger.LogInformation(
            "Topic group '{GroupId}': finalized after {Passes} pass(es) with {Items} item(s).",
            _context.TopicGroup.Id, _context.LoopCount, carried.Count);

        await context.YieldOutputAsync(result, cancellationToken);
    }
}
