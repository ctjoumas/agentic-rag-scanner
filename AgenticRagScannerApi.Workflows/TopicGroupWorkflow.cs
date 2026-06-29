using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Executors;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticRagScannerApi.Workflows;

/// <summary>
/// Builds the MAF workflow for a single topic group as the seven-executor agentic RAG loop:
/// QuerySynthesis → WebSearch → PreFilter → FetchAndClean → RelevanceEval → LoopController, where the
/// Loop Controller branches on its <see cref="Review.FinalDecision"/> - <c>Retry</c> loops back to
/// QuerySynthesis for another pass, <c>Finalize</c> exits to the Finalize tail, which yields the
/// <see cref="TopicGroupResult"/>. One workflow instance is built per topic group (parallel fan-out is
/// a later epic).
/// </summary>
public static class TopicGroupWorkflow
{
    /// <summary>The message that starts the loop (pass it as the workflow input).</summary>
    public static PassStart StartSignal { get; } = new();

    /// <summary>
    /// Builds the per-group workflow. Executors are created via
    /// <see cref="ActivatorUtilities.CreateInstance{T}(IServiceProvider, object[])"/> so each one gets
    /// the per-group <paramref name="context"/> by hand and its remaining dependencies (the wrapped
    /// agent/step plus its typed logger) resolved from <paramref name="serviceProvider"/>.
    /// </summary>
    public static Workflow Build(TopicGroupContext context, IServiceProvider serviceProvider)
    {
        var querySynthesis = ActivatorUtilities.CreateInstance<QuerySynthesisExecutor>(serviceProvider, context);
        var webSearch = ActivatorUtilities.CreateInstance<WebSearchExecutor>(serviceProvider, context);
        var preFilter = ActivatorUtilities.CreateInstance<PreFilterExecutor>(serviceProvider, context);
        var fetchAndClean = ActivatorUtilities.CreateInstance<FetchAndCleanExecutor>(serviceProvider, context);
        var relevanceEval = ActivatorUtilities.CreateInstance<RelevanceEvalExecutor>(serviceProvider, context);
        var loopController = ActivatorUtilities.CreateInstance<LoopControllerExecutor>(serviceProvider, context);
        var finalize = ActivatorUtilities.CreateInstance<FinalizeExecutor>(serviceProvider, context);

        return new WorkflowBuilder(querySynthesis)
            .AddEdge(querySynthesis, webSearch)
            .AddEdge(webSearch, preFilter)
            .AddEdge(preFilter, fetchAndClean)
            .AddEdge(fetchAndClean, relevanceEval)
            .AddEdge(relevanceEval, loopController)
            // The Loop Controller emits its Review; the two conditional edges route on its decision.
            .AddEdge<Review>(loopController, querySynthesis, condition: r => r.FinalDecision == LoopDecision.Retry)
            .AddEdge<Review>(loopController, finalize, condition: r => r.FinalDecision == LoopDecision.Finalize)
            .WithOutputFrom(finalize)
            .Build();
    }
}
