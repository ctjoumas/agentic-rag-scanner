using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Prompts;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Deterministic stub for <see cref="IImpactAreaAgent"/>: returns a canned impact area (from the
/// approved reference vocabulary) with no LLM call and no Cosmos dependency, so the workflow tests can
/// run the finalize chain end-to-end offline. The real agent (<see cref="ImpactAreaAgent"/>) loads the
/// closed set from Cosmos and classifies with the model.
/// </summary>
public sealed class ImpactAreaAgentStub : IImpactAreaAgent
{
    private const string CannedImpactArea = "Employer tax reporting/filing requirements";

    private readonly ILogger<ImpactAreaAgentStub> _logger;

    public ImpactAreaAgentStub(ILogger<ImpactAreaAgentStub> logger) => _logger = logger;

    public Task<string?> SelectAsync(
        IReadOnlyList<ResultItem> items,
        IReadOnlyDictionary<string, string?> fullTextByItemId,
        TopicGroupContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "ImpactArea stub ({PromptVersion}) for group '{GroupId}' over {Items} item(s).",
            ImpactAreaPrompt.Version, context.TopicGroup.Id, items.Count);

        return Task.FromResult<string?>(items.Count == 0 ? null : CannedImpactArea);
    }

    public Task<string?> SelectAsync(
        ResultItem item,
        string? fullText,
        TopicGroupContext context,
        CancellationToken cancellationToken = default)
        => SelectAsync(
            [item],
            new Dictionary<string, string?> { [item.Id] = fullText },
            context,
            cancellationToken);
}
