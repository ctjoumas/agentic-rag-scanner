using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Steps;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Executors;

/// <summary>
/// Step 6 of the seven-executor decomposition: the loop's branching node. Wraps
/// <see cref="ILoopController"/>: applies the per-group <see cref="TopicGroup.MaxLoops"/> cap and the
/// ≥80% recall override, maps the eval verdicts to vetted/discarded items (persisting carried full text
/// to blob), records the pass <see cref="Review"/>, and emits that <see cref="Review"/> so the two
/// conditional edges can route on its <see cref="Review.FinalDecision"/> - <c>Retry</c> loops back to
/// <see cref="QuerySynthesisExecutor"/>, <c>Finalize</c> exits to the finalize tail.
/// </summary>
/// <remarks>
/// Single input (<see cref="EvaluationResult"/>), single output (<see cref="Review"/>), so it uses the
/// <see cref="Executor{TInput, TOutput}"/> shortcut. The emitted message is the existing domain
/// <see cref="Review"/> rather than a wrapper record, because the conditional edges predicate directly on
/// <see cref="Review.FinalDecision"/>. The conditional edges and per-pass checkpointing are wired with the
/// workflow graph (a later step), not here.
/// </remarks>
public sealed class LoopControllerExecutor : Executor<EvaluationResult, Review>
{
    private readonly TopicGroupContext _context;
    private readonly ILoopController _loopController;
    private readonly ILogger<LoopControllerExecutor> _logger;

    public LoopControllerExecutor(
        TopicGroupContext context,
        ILoopController loopController,
        ILogger<LoopControllerExecutor> logger)
        : base($"loop-controller-{context.TopicGroup.Id}")
    {
        _context = context;
        _loopController = loopController;
        _logger = logger;
    }

    /// <summary>
    /// Reviews the current pass (cap + recall override + item routing + blob persistence), records the
    /// pass <see cref="Review"/>, and emits it for the conditional edges to route on.
    /// </summary>
    public override async ValueTask<Review> HandleAsync(EvaluationResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var decision = await _loopController.ReviewPassAsync(_context, message.Documents, message.Decision, cancellationToken);

        // ReviewPassAsync records the Review on the current pass as a side effect; emit that richer object
        // (the edges route on its FinalDecision, which equals the returned decision).
        var review = _context.History.CurrentPass!.Review!;

        _logger.LogDebug(
            "Loop controller for group '{GroupId}' pass {Pass}: {Decision}.",
            _context.TopicGroup.Id, _context.LoopCount, decision);

        return review;
    }
}
