using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Steps;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Executors;

/// <summary>
/// Step 3 of the seven-executor decomposition. Wraps <see cref="IPreFilterStep"/>: canonicalizes hit
/// URLs, drops invalid ones, and de-duplicates in-group and cross-group. This executor is the sole
/// writer of the pass's hits - it appends the kept hits to <see cref="LoopPass.Hits"/> on the current
/// pass before emitting <see cref="FilteredHitsResult"/> for the fetch-and-clean step.
/// </summary>
/// <remarks>
/// Single input (<see cref="HitsResult"/>), single output (<see cref="FilteredHitsResult"/>), so it uses
/// the <see cref="Executor{TInput, TOutput}"/> shortcut: override <see cref="HandleAsync"/> and the
/// returned value is forwarded to the connected fetch-and-clean executor.
/// </remarks>
public sealed class PreFilterExecutor : Executor<HitsResult, FilteredHitsResult>
{
    private readonly TopicGroupContext _context;
    private readonly IPreFilterStep _preFilter;
    private readonly ILogger<PreFilterExecutor> _logger;

    public PreFilterExecutor(
        TopicGroupContext context,
        IPreFilterStep preFilter,
        ILogger<PreFilterExecutor> logger)
        : base($"pre-filter-{context.TopicGroup.Id}")
    {
        _context = context;
        _preFilter = preFilter;
        _logger = logger;
    }

    /// <summary>
    /// Pre-filters the incoming hits, records the kept hits on the current pass, and emits them for
    /// the fetch-and-clean step.
    /// </summary>
    public override ValueTask<FilteredHitsResult> HandleAsync(HitsResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var filtered = _preFilter.Filter(message.Hits, _context);
        _context.History.CurrentPass!.Hits.AddRange(filtered);

        _logger.LogDebug(
            "Pre-filter for group '{GroupId}' pass {Pass}: {InCount} hit(s) -> {OutCount} kept.",
            _context.TopicGroup.Id, _context.LoopCount, message.Hits.Count, filtered.Count);

        return ValueTask.FromResult(new FilteredHitsResult(filtered));
    }
}
