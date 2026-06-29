using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Pipeline;
using AgenticRagScannerApi.Workflows.Steps;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Executors;

/// <summary>
/// Step 4 of the seven-executor decomposition. Wraps <see cref="IFetchAndCleanStep"/>: fetches and
/// cleans the full text for each filtered hit (a snippet fallback is flagged
/// <see cref="FetchedDocument.Unverified"/>, never dropped) and emits <see cref="DocumentsResult"/> for
/// the relevance-eval step.
/// </summary>
/// <remarks>
/// Single input (<see cref="FilteredHitsResult"/>), single output (<see cref="DocumentsResult"/>), so it
/// uses the <see cref="Executor{TInput, TOutput}"/> shortcut. The hits are fetched sequentially here;
/// the fetch step can parallelize internally later without changing the graph. The documents are
/// transient loop state, so they are not written to <see cref="TopicGroupContext.History"/>.
/// </remarks>
public sealed class FetchAndCleanExecutor : Executor<FilteredHitsResult, DocumentsResult>
{
    private readonly TopicGroupContext _context;
    private readonly IFetchAndCleanStep _fetchAndClean;
    private readonly ILogger<FetchAndCleanExecutor> _logger;

    public FetchAndCleanExecutor(
        TopicGroupContext context,
        IFetchAndCleanStep fetchAndClean,
        ILogger<FetchAndCleanExecutor> logger)
        : base($"fetch-clean-{context.TopicGroup.Id}")
    {
        _context = context;
        _fetchAndClean = fetchAndClean;
        _logger = logger;
    }

    /// <summary>
    /// Fetches and cleans the full text for each incoming hit and emits the documents for the
    /// relevance-eval step.
    /// </summary>
    public override async ValueTask<DocumentsResult> HandleAsync(FilteredHitsResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var documents = new List<FetchedDocument>(message.Hits.Count);
        foreach (var hit in message.Hits)
        {
            documents.Add(await _fetchAndClean.FetchAsync(hit, cancellationToken));
        }

        _logger.LogDebug(
            "Fetch & clean for group '{GroupId}' pass {Pass}: fetched {DocCount} document(s).",
            _context.TopicGroup.Id, _context.LoopCount, documents.Count);

        return new DocumentsResult(documents);
    }
}
