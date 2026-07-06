using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Tools;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Executors;

/// <summary>
/// Step 2 of the seven-executor decomposition. Wraps <see cref="IWebSearchAgent"/>: runs the
/// allowlist-scoped web search for the synthesized query and emits <see cref="HitsResult"/> for the
/// pre-filter step. Deliberately does not touch <see cref="TopicGroupContext.History"/> - the
/// pre-filter executor remains the sole writer of the pass's hits.
/// </summary>
/// <remarks>
/// Single input (<see cref="QueryResult"/>), single output (<see cref="HitsResult"/>), so it uses the
/// <see cref="Executor{TInput, TOutput}"/> shortcut: override <see cref="HandleAsync"/> and the
/// returned value is forwarded to the connected pre-filter executor.
/// </remarks>
public sealed class WebSearchExecutor : Executor<QueryResult, HitsResult>
{
    private readonly TopicGroupContext _context;
    private readonly IWebSearchAgent _agent;
    private readonly ILogger<WebSearchExecutor> _logger;

    public WebSearchExecutor(
        TopicGroupContext context,
        IWebSearchAgent agent,
        ILogger<WebSearchExecutor> logger)
        : base($"web-search-{context.TopicGroup.Id}")
    {
        _context = context;
        _agent = agent;
        _logger = logger;
    }

    /// <summary>
    /// Runs the web search for the incoming query, scoped to the run allowlist, and emits the raw hits
    /// for the pre-filter step.
    /// </summary>
    public override async ValueTask<HitsResult> HandleAsync(QueryResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var result = await _agent.SearchAsync(message.Query, _context.Run, cancellationToken);

        // Surface a genuine search failure (timeout/error) on the pass so a zero-result group caused by a
        // broken search is not silently reported as a clean, completed empty scan. The pre-filter remains
        // the sole writer of the pass's hits; here we only flag the failure.
        if (result.Failed && _context.History.CurrentPass is { } pass)
        {
            pass.SearchFailed = true;
            pass.SearchFailureReason = result.FailureReason;

            _logger.LogWarning(
                "Web search for group '{GroupId}' pass {Pass} failed: {Reason}",
                _context.TopicGroup.Id, _context.LoopCount, result.FailureReason);
        }

        _logger.LogDebug(
            "Web search for group '{GroupId}' pass {Pass}: '{Query}' returned {HitCount} hit(s).",
            _context.TopicGroup.Id, _context.LoopCount, message.Query, result.Hits.Count);

        return new HitsResult(result.Hits);
    }
}
