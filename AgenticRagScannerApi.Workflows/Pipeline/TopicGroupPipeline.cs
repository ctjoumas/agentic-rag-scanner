using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Agents;
using AgenticRagScannerApi.Workflows.Steps;
using AgenticRagScannerApi.Workflows.Tools;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Pipeline;

/// <summary>
/// Encapsulates one topic group's agentic RAG loop in the frozen order, threading
/// <see cref="SearchHistory"/> through each pass:
/// QuerySynthesis → BingSearch(tool) → Pre-filter → Fetch&amp;Clean → RelevanceEval → LoopController,
/// then (on finalize) VerdictRouting → Enrichment → Categorize → Summarize&amp;Impact.
/// The MAF executor is a thin adapter over this pipeline, so the loop is unit-testable without
/// standing up a workflow.
/// </summary>
public sealed class TopicGroupPipeline
{
    private readonly IQuerySynthesisAgent _querySynthesis;
    private readonly IBingSearchTool _bingSearch;
    private readonly IPreFilterStep _preFilter;
    private readonly IFetchAndCleanStep _fetchAndClean;
    private readonly IRelevanceEvalAgent _relevanceEval;
    private readonly ILoopController _loopController;
    private readonly IVerdictRouting _verdictRouting;
    private readonly IEnrichmentAgent _enrichment;
    private readonly ICategorizeAgent _categorize;
    private readonly ISummarizeImpactAgent _summarize;
    private readonly ILogger<TopicGroupPipeline> _logger;

    public TopicGroupPipeline(
        IQuerySynthesisAgent querySynthesis,
        IBingSearchTool bingSearch,
        IPreFilterStep preFilter,
        IFetchAndCleanStep fetchAndClean,
        IRelevanceEvalAgent relevanceEval,
        ILoopController loopController,
        IVerdictRouting verdictRouting,
        IEnrichmentAgent enrichment,
        ICategorizeAgent categorize,
        ISummarizeImpactAgent summarize,
        ILogger<TopicGroupPipeline> logger)
    {
        _querySynthesis = querySynthesis;
        _bingSearch = bingSearch;
        _preFilter = preFilter;
        _fetchAndClean = fetchAndClean;
        _relevanceEval = relevanceEval;
        _loopController = loopController;
        _verdictRouting = verdictRouting;
        _enrichment = enrichment;
        _categorize = categorize;
        _summarize = summarize;
        _logger = logger;
    }

    /// <summary>
    /// Runs one loop pass and returns the controller's decision. The pass is recorded in
    /// <see cref="SearchHistory.Passes"/> (the source of truth for <see cref="TopicGroupContext.LoopCount"/>
    /// and <see cref="TopicGroupContext.ShouldContinue"/>).
    /// </summary>
    public async Task<LoopDecision> RunPassAsync(TopicGroupContext context, CancellationToken cancellationToken = default)
    {
        // 1. Query synthesis - reads SearchHistory to rotate synonyms / avoid redundancy. One query
        //    per pass: breadth comes from the agentic loop re-synthesizing on later passes.
        var query = await _querySynthesis.SynthesizeAsync(context, cancellationToken);

        // Start the pass and append it so LoopCount reflects this pass during eval/control.
        var pass = new LoopPass { Pass = context.LoopCount + 1, Query = query };
        context.History.Passes.Add(pass);

        // 2. Bing search (allowlist-gated tool).
        var hits = await _bingSearch.SearchAsync(query, context.Run, cancellationToken);

        // 3. Deterministic pre-filter (dedupe incl. earlier passes + cross-group + URL validity).
        var filtered = _preFilter.Filter(hits, context);
        pass.Hits.AddRange(filtered);

        // 4. Full-text fetch & clean (snippet fallback flags Unverified, never drops).
        var documents = new List<FetchedDocument>(filtered.Count);
        foreach (var hit in filtered)
        {
            documents.Add(await _fetchAndClean.FetchAsync(hit, cancellationToken));
        }

        // 5. Relevance eval (full text + dates + history) -> per-item verdicts + raw decision.
        var decision = await _relevanceEval.EvaluateAsync(context, documents, cancellationToken);

        // 6. Loop controller - records the pass Review and decides retry/finalize (honors maxLoops).
        var loopDecision = _loopController.ReviewPass(context, documents, decision);

        _logger.LogDebug(
            "Pipeline pass {Pass} for group '{GroupId}': {Hits} hit(s) -> {Decision}.",
            pass.Pass, context.TopicGroup.Id, filtered.Count, loopDecision);

        return loopDecision;
    }

    /// <summary>
    /// Runs verdict routing then the enrichment chain (Enrichment → Categorize → Summarize&amp;Impact)
    /// over the carried (RELEVANT/BORDERLINE) items, returning them for the group result.
    /// </summary>
    public async Task<IReadOnlyList<ResultItem>> FinalizeAsync(TopicGroupContext context, CancellationToken cancellationToken = default)
    {
        var carried = _verdictRouting.Route(context);

        foreach (var item in carried)
        {
            await _enrichment.EnrichAsync(item, context, cancellationToken);
            await _categorize.CategorizeAsync(item, cancellationToken);
            await _summarize.SummarizeAsync(item, context, cancellationToken);
        }

        return carried;
    }
}
