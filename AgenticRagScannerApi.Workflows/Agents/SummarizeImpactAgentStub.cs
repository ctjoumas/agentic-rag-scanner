using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Prompts;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Epic 2 stub for <see cref="ISummarizeImpactAgent"/>: stamps a canned plain-English impact summary
/// onto an enriched item - no LLM call. The real agent (Epic 7) does RAG over the in-memory search
/// history and frames the change around its effective date.
/// </summary>
public sealed class SummarizeImpactAgentStub : ISummarizeImpactAgent
{
    private readonly ILogger<SummarizeImpactAgentStub> _logger;

    public SummarizeImpactAgentStub(ILogger<SummarizeImpactAgentStub> logger) => _logger = logger;

    public Task<ResultItem> SummarizeAsync(ResultItem item, TopicGroupContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "SummarizeImpact stub ({PromptVersion}) for group '{GroupId}', item '{ItemId}'.",
            SummarizeImpactPrompt.Version, context.TopicGroup.Id, item.Id);

        item.ImpactSummary = "Canned impact summary (Epic 2): plain-English impact framing for the audience.";

        return Task.FromResult(item);
    }
}
