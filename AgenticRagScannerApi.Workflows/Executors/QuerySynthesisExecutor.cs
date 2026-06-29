using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Agents;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Executors;

/// <summary>
/// Step 1 of the seven-executor decomposition. Wraps <see cref="IQuerySynthesisAgent"/>: synthesizes a
/// single non-redundant query for the current pass and starts the pass by appending a
/// <see cref="LoopPass"/> to the shared <see cref="TopicGroupContext.History"/> (the same bookkeeping
/// the monolithic pipeline does today). Emits <see cref="QueryResult"/> for the web-search step.
/// </summary>
/// <remarks>
/// Two entry points feed this executor: <see cref="PassStart"/> begins the first pass, and a
/// <see cref="Review"/> routed back from the Loop Controller (on a retry decision) begins a re-loop.
/// Both just re-synthesize against the updated history.
/// </remarks>
public sealed class QuerySynthesisExecutor : Executor
{
    private readonly TopicGroupContext _context;
    private readonly IQuerySynthesisAgent _agent;
    private readonly ILogger<QuerySynthesisExecutor> _logger;

    public QuerySynthesisExecutor(
        TopicGroupContext context,
        IQuerySynthesisAgent agent,
        ILogger<QuerySynthesisExecutor> logger)
        : base($"query-synthesis-{context.TopicGroup.Id}")
    {
        _context = context;
        _agent = agent;
        _logger = logger;
    }

    /// <summary>
    /// Registers the two entry points: <see cref="PassStart"/> (first pass) and a <see cref="Review"/>
    /// routed back from the Loop Controller (retry). Both emit <see cref="QueryResult"/>, which the route
    /// builder forwards to the connected web-search executor.
    /// </summary>
    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocol) =>
        protocol.ConfigureRoutes(routes => routes
            .AddHandler<PassStart, QueryResult>(HandleStartAsync)
            .AddHandler<Review, QueryResult>(HandleRetryAsync));

    /// <summary>First pass: the workflow starts here.</summary>
    private ValueTask<QueryResult> HandleStartAsync(PassStart message, IWorkflowContext context, CancellationToken cancellationToken)
        => SynthesizeAsync(cancellationToken);

    /// <summary>Retry pass: the Loop Controller routed its <see cref="Review"/> back here to re-synthesize.</summary>
    private ValueTask<QueryResult> HandleRetryAsync(Review message, IWorkflowContext context, CancellationToken cancellationToken)
        => SynthesizeAsync(cancellationToken);

    private async ValueTask<QueryResult> SynthesizeAsync(CancellationToken cancellationToken)
    {
        var synthesis = await _agent.SynthesizeAsync(_context, cancellationToken);

        // Start the pass and append it so LoopCount reflects this pass during the rest of the loop.
        _context.History.Passes.Add(new LoopPass
        {
            Pass = _context.LoopCount + 1,
            Query = synthesis.Query,
            QueryRationale = synthesis.Rationale,
        });

        _logger.LogDebug(
            "Query synthesis for group '{GroupId}' pass {Pass}: '{Query}'.",
            _context.TopicGroup.Id, _context.LoopCount, synthesis.Query);

        return new QueryResult(synthesis.Query, synthesis.Rationale);
    }

    /// <summary>
    /// Persists the shared loop history so the run can resume from this point. This executor is the
    /// single checkpoint owner for the group: the framework invokes the hook on every executor each
    /// checkpoint, so to avoid duplicate writes to the shared <c>SearchHistory</c> key only one
    /// executor writes it. All executors share the same <see cref="TopicGroupContext.History"/>, so the
    /// snapshot taken here reflects whatever step ran this super-step.
    /// </summary>
    protected override ValueTask OnCheckpointingAsync(IWorkflowContext workflowContext, CancellationToken cancellationToken = default) =>
        SearchHistoryCheckpoint.SaveAsync(workflowContext, _context, cancellationToken);

    /// <summary>Rehydrates the shared loop history when a run is resumed (idempotent).</summary>
    protected override ValueTask OnCheckpointRestoredAsync(IWorkflowContext workflowContext, CancellationToken cancellationToken = default) =>
        SearchHistoryCheckpoint.RestoreAsync(workflowContext, _context, cancellationToken);
}
