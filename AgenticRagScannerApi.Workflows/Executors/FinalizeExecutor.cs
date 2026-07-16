using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Agents;
using AgenticRagScannerApi.Workflows.CompanyView;
using AgenticRagScannerApi.Workflows.Configuration;
using AgenticRagScannerApi.Workflows.Pipeline;
using AgenticRagScannerApi.Workflows.Steps;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Workflows.Executors;

/// <summary>
/// Step 7 of the seven-executor decomposition: the loop's terminal tail. Reached only via the
/// <see cref="LoopControllerExecutor"/> <c>Finalize</c> conditional edge. Routes the vetted verdicts
/// (<see cref="IVerdictRouting"/>), reads back each carried item's vetted full-text snapshot, runs the
/// per-item enrichment step over the carried items, then categorizes EACH vetted document individually -
/// its own Impact Area, Tags, and Company View, produced from that document's own full text. The
/// jurisdiction-scoped prior Company View exemplars are fetched once per group and passed into every
/// item's call. Yields the aggregated <see cref="TopicGroupResult"/> as the workflow output.
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
    private readonly IImpactAreaAgent _impactArea;
    private readonly ITagsAgent _tags;
    private readonly ICompanyViewAgent _companyView;
    private readonly IPriorCompanyViewSource _priorViews;
    private readonly CompanyViewOptions _companyViewOptions;
    private readonly IFullTextStore _fullTextStore;
    private readonly ILogger<FinalizeExecutor> _logger;

    public FinalizeExecutor(
        TopicGroupContext context,
        IVerdictRouting verdictRouting,
        IEnrichmentAgent enrichment,
        IImpactAreaAgent impactArea,
        ITagsAgent tags,
        ICompanyViewAgent companyView,
        IPriorCompanyViewSource priorViews,
        IOptions<CompanyViewOptions> companyViewOptions,
        IFullTextStore fullTextStore,
        ILogger<FinalizeExecutor> logger)
        : base($"finalize-{context.TopicGroup.Id}")
    {
        _context = context;
        _verdictRouting = verdictRouting;
        _enrichment = enrichment;
        _impactArea = impactArea;
        _tags = tags;
        _companyView = companyView;
        _priorViews = priorViews;
        _companyViewOptions = companyViewOptions.Value;
        _fullTextStore = fullTextStore;
        _logger = logger;
    }

    /// <summary>
    /// Routes verdicts, runs the enrichment chain over the carried items, and yields the group result.
    /// </summary>
    public override async ValueTask HandleAsync(Review message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var carried = _verdictRouting.Route(_context);

        // Read back each carried item's vetted full-text snapshot once (from blob) so the post-loop work
        // grounds on exactly what the eval agent read; null when the fetch had failed (metadata only).
        var fullTextByItemId = new Dictionary<string, string?>(carried.Count);
        foreach (var item in carried)
        {
            var fullText = await ReadFullTextAsync(item, cancellationToken);
            fullTextByItemId[item.Id] = fullText;

            await _enrichment.EnrichAsync(item, _context, cancellationToken);
        }

        // Categorize EACH vetted document individually: its own Impact Area (single-label), Tags (multi-label),
        // and Company View, each grounded on THAT document's own full text. The prior Company View exemplars
        // are jurisdiction-scoped, so they are fetched ONCE per group and passed into every item's call (see
        // docs/company-view-per-doc-implementation-plan.md §3.4a). Items are processed sequentially for now;
        // per-item fan-out under the shared throttle is deferred to Phase 13. Skipped entirely when the group
        // carried nothing.
        if (carried.Count > 0)
        {
            var priorViews = await _priorViews.GetByJurisdictionAsync(_context.Run.Jurisdiction, cancellationToken);
            var exemplars = priorViews.Count > _companyViewOptions.MaxExemplars
                ? priorViews.Take(_companyViewOptions.MaxExemplars).ToList()
                : priorViews;

            foreach (var item in carried)
            {
                var fullText = fullTextByItemId[item.Id];

                // Impact area and tags are independent, so run them concurrently; the Company View then
                // grounds on the item's full text plus its own impact area and tags.
                var impactAreaTask = _impactArea.SelectAsync(item, fullText, _context, cancellationToken);
                var tagsTask = _tags.SelectAsync(item, fullText, _context, cancellationToken);
                await Task.WhenAll(impactAreaTask, tagsTask);
                var impactArea = await impactAreaTask;
                var tags = await tagsTask;

                item.CompanyView = await _companyView.GenerateAsync(
                    item, fullText, impactArea, tags, exemplars, _context, cancellationToken);
            }
        }

        // A group that carried nothing *because* its final web search failed (timeout/error) is not a clean
        // empty scan - report it as Failed so the caller can distinguish "search worked, found nothing" from
        // "search never ran". We key off the LAST pass only: an earlier pass that failed transiently but was
        // retried and recovered should not taint the outcome. A genuine empty result (final search succeeded,
        // zero citations) stays Completed.
        var searchFailed = _context.History.CurrentPass?.SearchFailed == true;
        var status = carried.Count == 0 && searchFailed ? "Failed" : "Completed";

        var result = new TopicGroupResult
        {
            GroupId = _context.TopicGroup.Id,
            GroupName = _context.TopicGroup.Name,
            Status = status,
            LoopCount = _context.LoopCount,
            Items = carried,
            History = SearchHistorySerializer.ToSnapshot(_context.History),
        };

        if (status == "Failed")
        {
            _logger.LogWarning(
                "Topic group '{GroupId}': finalized as Failed after {Passes} pass(es) - carried no items because web search failed.",
                _context.TopicGroup.Id, _context.LoopCount);
        }
        else
        {
            _logger.LogInformation(
                "Topic group '{GroupId}': finalized after {Passes} pass(es) with {Items} item(s).",
                _context.TopicGroup.Id, _context.LoopCount, carried.Count);
        }

        await context.YieldOutputAsync(result, cancellationToken);
    }

    /// <summary>
    /// Reads back the item's vetted full-text snapshot from blob when one was persisted
    /// (<see cref="ResultItem.FullTextBlobUri"/> is set), or returns <see langword="null"/> otherwise.
    /// </summary>
    private async Task<string?> ReadFullTextAsync(ResultItem item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.FullTextBlobUri))
        {
            return null;
        }

        return await _fullTextStore.ReadAsync(item.RunId, item.GroupId, item.Id, cancellationToken);
    }
}
