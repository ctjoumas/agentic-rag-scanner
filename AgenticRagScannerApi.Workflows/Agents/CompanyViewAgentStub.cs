using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Prompts;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Deterministic stub for <see cref="ICompanyViewAgent"/>: returns a canned aggregate
/// <see cref="CompanyViewRecord"/> with no LLM call and no retrieval dependency, so the workflow tests
/// can run the finalize chain end-to-end offline. The real agent (<see cref="CompanyViewAgent"/>)
/// retrieves prior records by jurisdiction and synthesizes the view with the model.
/// </summary>
public sealed class CompanyViewAgentStub : ICompanyViewAgent
{
    private readonly ILogger<CompanyViewAgentStub> _logger;

    public CompanyViewAgentStub(ILogger<CompanyViewAgentStub> logger) => _logger = logger;

    public Task<CompanyViewRecord?> GenerateAsync(
        IReadOnlyList<ResultItem> items,
        IReadOnlyDictionary<string, string?> fullTextByItemId,
        string? impactArea,
        IReadOnlyList<string> tags,
        TopicGroupContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "CompanyView stub ({PromptVersion}) for group '{GroupId}' over {Items} item(s).",
            CompanyViewPrompt.Version, context.TopicGroup.Id, items.Count);

        if (items.Count == 0)
        {
            return Task.FromResult<CompanyViewRecord?>(null);
        }

        var record = new CompanyViewRecord
        {
            Jurisdiction = context.Run.Jurisdiction,
            ImpactArea = impactArea,
            Tags = tags,
            TitleOfUpdate = $"Canned aggregate view for {context.TopicGroup.Name}",
            SummaryOfUpdate = "Canned consolidated summary (stub) across the group's updates.",
            CompanyView = "Canned Company View (stub): practitioner-style advice aggregating the group's updates.",
            SupportingReference = string.Join(" | ", items.SelectMany(i => i.SourceUrls)),
        };

        return Task.FromResult<CompanyViewRecord?>(record);
    }

    public Task<CompanyViewRecord?> GenerateAsync(
        ResultItem item,
        string? fullText,
        string? impactArea,
        IReadOnlyList<string> tags,
        IReadOnlyList<CompanyViewRecord> priorViews,
        TopicGroupContext context,
        CancellationToken cancellationToken = default)
        => GenerateAsync(
            [item],
            new Dictionary<string, string?> { [item.Id] = fullText },
            impactArea,
            tags,
            context,
            cancellationToken);
}
