using System.Text.Json;
using System.Text.Json.Serialization;
using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Common;
using AgenticRagScannerApi.Workflows.Prompts;
using AgenticRagScannerApi.Workflows.Vocabulary;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Epic 8 (story 8.3) real implementation of <see cref="ITagsAgent"/>: a MAF
/// <see cref="ChatClientAgent"/> over the shared Foundry model deployment (<see cref="IChatClient"/>).
/// It makes a single Structured Outputs call - once per vetted document, separate from the Impact Area
/// agent - that selects zero or more tags from the approved vocabulary (loaded from Cosmos via
/// <see cref="IRegulatoryVocabularyProvider"/>), grounded on that document's vetted full-text snapshot.
/// Each returned tag is validated against the controlled vocabulary and normalized to its canonical
/// spelling; off-list values are dropped (never invented) and a failed call returns an empty list.
/// </summary>
public sealed class TagsAgent : ITagsAgent
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Low temperature so the multi-label selection stays deterministic and grounded.</summary>
    private const float Temperature = 0.1f;

    private readonly IChatClient _chatClient;
    private readonly IRegulatoryVocabularyProvider _vocabulary;
    private readonly ILogger<TagsAgent> _logger;

    public TagsAgent(
        IChatClient chatClient,
        IRegulatoryVocabularyProvider vocabulary,
        ILogger<TagsAgent> logger)
    {
        _chatClient = chatClient;
        _vocabulary = vocabulary;
        _logger = logger;
    }

    /// <summary>Core multi-label selection over a one-item list; the per-document public overload wraps
    /// this. Returns the canonical tags (possibly empty); empty also when the vocabulary is empty or the
    /// model call failed.</summary>
    private async Task<IReadOnlyList<string>> SelectAsync(
        IReadOnlyList<ResultItem> items,
        IReadOnlyDictionary<string, string?> fullTextByItemId,
        TopicGroupContext context,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var tags = await _vocabulary.GetTagsAsync(cancellationToken);
        if (tags.Count == 0)
        {
            _logger.LogWarning(
                "Tags ({PromptVersion}) for group '{GroupId}': tag vocabulary is empty; leaving tags unset.",
                TagsPrompt.Version, context.TopicGroup.Id);
            return [];
        }

        var updatesBlock = AggregateContextBuilder.BuildUpdatesBlock(items, fullTextByItemId);
        var systemPrompt = TagsPrompt.BuildSystemPrompt(context.Run.Jurisdiction, tags);
        var userPrompt = TagsPrompt.BuildUserPrompt(context, updatesBlock);

        var agent = new ChatClientAgent(_chatClient, new ChatClientAgentOptions
        {
            Name = "Tags",
            ChatOptions = new ChatOptions
            {
                Instructions = systemPrompt,
                Temperature = Temperature,
            },
        });

        try
        {
            var response = await agent.RunAsync<TagsResult>(
                userPrompt,
                serializerOptions: s_jsonOptions,
                cancellationToken: cancellationToken);

            var selected = Normalize(response.Result?.Tags, tags);
            _logger.LogInformation(
                "Tags ({PromptVersion}) for group '{GroupId}': assigned {Count} tag(s) [{Tags}] across {Items} update(s).",
                TagsPrompt.Version, context.TopicGroup.Id, selected.Count, string.Join(", ", selected), items.Count);
            return selected;
        }
        catch (Exception ex) when (ChatFailure.IsDegradable(ex, cancellationToken))
        {
            _logger.LogWarning(
                ex,
                "Tags ({PromptVersion}) for group '{GroupId}': tagging call failed; leaving tags unset.",
                TagsPrompt.Version, context.TopicGroup.Id);
        }

        return [];
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> SelectAsync(
        ResultItem item,
        string? fullText,
        TopicGroupContext context,
        CancellationToken cancellationToken = default)
        => SelectAsync(
            [item],
            new Dictionary<string, string?> { [item.Id] = fullText },
            context,
            cancellationToken);

    /// <summary>
    /// Validates each model-selected tag against the controlled vocabulary and returns the canonical
    /// spellings (case/whitespace-insensitive), dropping blanks, off-list values, and duplicates while
    /// preserving order.
    /// </summary>
    private static IReadOnlyList<string> Normalize(IEnumerable<string>? values, IReadOnlyList<string> vocabulary)
    {
        if (values is null)
        {
            return [];
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            var canonical = vocabulary.FirstOrDefault(tag => string.Equals(tag, trimmed, StringComparison.OrdinalIgnoreCase));
            if (canonical is not null && seen.Add(canonical))
            {
                result.Add(canonical);
            }
        }

        return result;
    }

    private sealed record TagsResult(
        [property: JsonPropertyName("tags")] string[]? Tags,
        [property: JsonPropertyName("rationale")] string? Rationale);
}
