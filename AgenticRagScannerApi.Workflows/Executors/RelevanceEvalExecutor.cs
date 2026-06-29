using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Agents;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Executors;

/// <summary>
/// Step 5 of the seven-executor decomposition. Wraps <see cref="IRelevanceEvalAgent"/>: makes the
/// single full-text relevance call over the current pass's documents and emits the raw
/// <see cref="ReviewDecision"/> (per-item verdicts plus the agent's loop decision) for the
/// loop-controller step.
/// </summary>
/// <remarks>
/// Single input (<see cref="DocumentsResult"/>), single output (<see cref="EvaluationResult"/>), so it
/// uses the <see cref="Executor{TInput, TOutput}"/> shortcut. The documents ride along in the outgoing
/// message because the loop controller needs both them and the decision; nothing is written to
/// <see cref="TopicGroupContext.History"/> here - the loop controller records the pass's
/// <see cref="LoopPass.Review"/>.
/// </remarks>
public sealed class RelevanceEvalExecutor : Executor<DocumentsResult, EvaluationResult>
{
    private readonly TopicGroupContext _context;
    private readonly IRelevanceEvalAgent _agent;
    private readonly ILogger<RelevanceEvalExecutor> _logger;

    public RelevanceEvalExecutor(
        TopicGroupContext context,
        IRelevanceEvalAgent agent,
        ILogger<RelevanceEvalExecutor> logger)
        : base($"relevance-eval-{context.TopicGroup.Id}")
    {
        _context = context;
        _agent = agent;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates the current pass's documents and emits the raw decision plus the documents for the
    /// loop-controller step.
    /// </summary>
    public override async ValueTask<EvaluationResult> HandleAsync(DocumentsResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var decision = await _agent.EvaluateAsync(_context, message.Documents, cancellationToken);

        _logger.LogDebug(
            "Relevance eval for group '{GroupId}' pass {Pass}: decision {Decision} over {ItemCount} item(s).",
            _context.TopicGroup.Id, _context.LoopCount, decision.Decision, decision.Items.Count);

        return new EvaluationResult(message.Documents, decision);
    }
}
