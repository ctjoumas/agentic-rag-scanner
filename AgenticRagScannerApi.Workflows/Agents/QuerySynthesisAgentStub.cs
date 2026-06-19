using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Prompts;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Epic 2 stub for <see cref="IQuerySynthesisAgent"/>: returns a single canned query derived from the
/// topic group's keywords - no LLM call. The query embeds the pass number so successive re-loops
/// produce a different (non-redundant) query, exercising the SearchHistory-aware loop. Replaced by
/// the real Foundry-backed agent in Epic 3.
/// </summary>
public sealed class QuerySynthesisAgentStub : IQuerySynthesisAgent
{
    private readonly ILogger<QuerySynthesisAgentStub> _logger;

    public QuerySynthesisAgentStub(ILogger<QuerySynthesisAgentStub> logger) => _logger = logger;

    public Task<string> SynthesizeAsync(TopicGroupContext context, CancellationToken cancellationToken = default)
    {
        var pass = context.LoopCount + 1;
        var keywords = context.TopicGroup.Keywords;
        var primary = keywords.Count > 0 ? keywords[0] : context.TopicGroup.Name;

        _logger.LogDebug(
            "QuerySynthesis stub ({PromptVersion}) for group '{GroupId}', pass {Pass}.",
            QuerySynthesisPrompt.Version, context.TopicGroup.Id, pass);

        var query = $"{primary} {context.Run.Jurisdiction} update (pass {pass})";

        return Task.FromResult(query);
    }
}
