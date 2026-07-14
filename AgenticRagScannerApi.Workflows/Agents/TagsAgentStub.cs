using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Prompts;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Deterministic stub for <see cref="ITagsAgent"/>: returns a canned set of approved tags with no LLM
/// call and no Cosmos dependency, so the workflow tests can run the finalize chain end-to-end offline.
/// The real agent (<see cref="TagsAgent"/>) loads the controlled vocabulary from Cosmos and selects with
/// the model.
/// </summary>
public sealed class TagsAgentStub : ITagsAgent
{
    private static readonly IReadOnlyList<string> CannedTags = ["Payroll Reporting", "National Insurance"];

    private readonly ILogger<TagsAgentStub> _logger;

    public TagsAgentStub(ILogger<TagsAgentStub> logger) => _logger = logger;

    public Task<IReadOnlyList<string>> SelectAsync(
        IReadOnlyList<ResultItem> items,
        IReadOnlyDictionary<string, string?> fullTextByItemId,
        TopicGroupContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Tags stub ({PromptVersion}) for group '{GroupId}' over {Items} item(s).",
            TagsPrompt.Version, context.TopicGroup.Id, items.Count);

        return Task.FromResult<IReadOnlyList<string>>(items.Count == 0 ? [] : CannedTags);
    }
}
