using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Prompts;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Deterministic stub for <see cref="IDeloitteViewAgent"/>: returns a canned aggregate
/// <see cref="DeloitteViewRecord"/> with no LLM call and no retrieval dependency, so the workflow tests
/// can run the finalize chain end-to-end offline. The real agent (<see cref="DeloitteViewAgent"/>)
/// retrieves prior records by jurisdiction and synthesizes the view with the model.
/// </summary>
public sealed class DeloitteViewAgentStub : IDeloitteViewAgent
{
    private readonly ILogger<DeloitteViewAgentStub> _logger;

    public DeloitteViewAgentStub(ILogger<DeloitteViewAgentStub> logger) => _logger = logger;

    public Task<DeloitteViewRecord?> GenerateAsync(
        IReadOnlyList<ResultItem> items,
        IReadOnlyDictionary<string, string?> fullTextByItemId,
        string? impactArea,
        IReadOnlyList<string> tags,
        TopicGroupContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "DeloitteView stub ({PromptVersion}) for group '{GroupId}' over {Items} item(s).",
            DeloitteViewPrompt.Version, context.TopicGroup.Id, items.Count);

        if (items.Count == 0)
        {
            return Task.FromResult<DeloitteViewRecord?>(null);
        }

        var record = new DeloitteViewRecord
        {
            Jurisdiction = context.Run.Jurisdiction,
            ImpactArea = impactArea,
            Tags = tags,
            TitleOfUpdate = $"Canned aggregate view for {context.TopicGroup.Name}",
            SummaryOfUpdate = "Canned consolidated summary (stub) across the group's updates.",
            DeloitteView = "Canned Deloitte View (stub): practitioner-style advice aggregating the group's updates.",
            SupportingReference = string.Join(" | ", items.SelectMany(i => i.SourceUrls)),
        };

        return Task.FromResult<DeloitteViewRecord?>(record);
    }
}
