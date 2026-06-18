using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Prompts;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Epic 2 stub for <see cref="IEnrichmentAgent"/>: stamps a canned "what it does" summary onto a
/// carried item - no LLM call. Relevance is already decided upstream, so this never re-judges the
/// item. Replaced by the real Foundry-backed agent in Epic 7.
/// </summary>
public sealed class EnrichmentAgentStub : IEnrichmentAgent
{
    private readonly ILogger<EnrichmentAgentStub> _logger;

    public EnrichmentAgentStub(ILogger<EnrichmentAgentStub> logger) => _logger = logger;

    public Task<ResultItem> EnrichAsync(ResultItem item, TopicGroupContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Enrichment stub ({PromptVersion}) for group '{GroupId}', item '{ItemId}'.",
            EnrichmentPrompt.Version, context.TopicGroup.Id, item.Id);

        item.WhatItDoes = "Canned enrichment (Epic 2): plain-English description of what this item does.";

        return Task.FromResult(item);
    }
}
