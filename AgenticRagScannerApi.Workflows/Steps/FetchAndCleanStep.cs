using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Workflows.Pipeline;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Steps;

/// <inheritdoc />
public sealed class FetchAndCleanStep : IFetchAndCleanStep
{
    private readonly ILogger<FetchAndCleanStep> _logger;

    public FetchAndCleanStep(ILogger<FetchAndCleanStep> logger) => _logger = logger;

    public Task<FetchedDocument> FetchAsync(SearchHit hit, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetch & clean stub for {Url}.", hit.Url);

        // Epic 2 scaffold: no real HTTP fetch. Use the Bing snippet as the canned "cleaned" text and
        // mark it verified (the Unverified fallback path is exercised for real in Epic 5).
        var document = new FetchedDocument
        {
            Hit = hit,
            CleanedText = string.IsNullOrWhiteSpace(hit.Snippet)
                ? $"Canned cleaned text (Epic 2) for {hit.Url}."
                : hit.Snippet,
            Unverified = false,
        };

        return Task.FromResult(document);
    }
}
