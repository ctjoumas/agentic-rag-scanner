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
/// Epic 8 (story 8.2) real implementation of <see cref="IImpactAreaAgent"/>: a MAF
/// <see cref="ChatClientAgent"/> over the shared Foundry model deployment (<see cref="IChatClient"/>).
/// It makes a single Structured Outputs call - once per vetted document - that picks one impact area from
/// the approved vocabulary (loaded from Cosmos via <see cref="IRegulatoryVocabularyProvider"/>), grounded
/// on that document's vetted full-text snapshot. The model's choice is validated against the closed set
/// and normalized to the canonical vocabulary spelling; an off-list or failed result returns
/// <see langword="null"/> (a wrong single-label guess is worse than none in a compliance context) and
/// logs a warning.
/// </summary>
public sealed class ImpactAreaAgent : IImpactAreaAgent
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Low temperature so the single-label choice stays deterministic and grounded.</summary>
    private const float Temperature = 0.1f;

    private readonly IChatClient _chatClient;
    private readonly IRegulatoryVocabularyProvider _vocabulary;
    private readonly ILogger<ImpactAreaAgent> _logger;

    public ImpactAreaAgent(
        IChatClient chatClient,
        IRegulatoryVocabularyProvider vocabulary,
        ILogger<ImpactAreaAgent> logger)
    {
        _chatClient = chatClient;
        _vocabulary = vocabulary;
        _logger = logger;
    }

    /// <summary>Core single-label classification over a one-item list; the per-document public overload
    /// wraps this. Returns the canonical impact area, or null when the vocabulary is empty or the model
    /// returned an off-list/failed result.</summary>
    private async Task<string?> SelectAsync(
        IReadOnlyList<ResultItem> items,
        IReadOnlyDictionary<string, string?> fullTextByItemId,
        TopicGroupContext context,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            return null;
        }

        var impactAreas = await _vocabulary.GetImpactAreasAsync(cancellationToken);
        if (impactAreas.Count == 0)
        {
            _logger.LogWarning(
                "ImpactArea ({PromptVersion}) for group '{GroupId}': impact-area vocabulary is empty; leaving impact area unset.",
                ImpactAreaPrompt.Version, context.TopicGroup.Id);
            return null;
        }

        var updatesBlock = AggregateContextBuilder.BuildUpdatesBlock(items, fullTextByItemId);
        var systemPrompt = ImpactAreaPrompt.BuildSystemPrompt(context.Run.Jurisdiction, impactAreas);
        var userPrompt = ImpactAreaPrompt.BuildUserPrompt(context, updatesBlock);

        var agent = new ChatClientAgent(_chatClient, new ChatClientAgentOptions
        {
            Name = "ImpactArea",
            ChatOptions = new ChatOptions
            {
                Instructions = systemPrompt,
                Temperature = Temperature,
            },
        });

        try
        {
            var response = await agent.RunAsync<ImpactAreaResult>(
                userPrompt,
                serializerOptions: s_jsonOptions,
                cancellationToken: cancellationToken);

            var chosen = Normalize(response.Result?.ImpactArea, impactAreas);
            if (chosen is not null)
            {
                _logger.LogInformation(
                    "ImpactArea ({PromptVersion}) for group '{GroupId}': assigned '{ImpactArea}' across {Items} update(s).",
                    ImpactAreaPrompt.Version, context.TopicGroup.Id, chosen, items.Count);
                return chosen;
            }

            _logger.LogWarning(
                "ImpactArea ({PromptVersion}) for group '{GroupId}': model returned an off-list value '{Value}'; leaving impact area unset.",
                ImpactAreaPrompt.Version, context.TopicGroup.Id, response.Result?.ImpactArea ?? "(null)");
        }
        catch (Exception ex) when (ChatFailure.IsDegradable(ex, cancellationToken))
        {
            _logger.LogWarning(
                ex,
                "ImpactArea ({PromptVersion}) for group '{GroupId}': classification call failed; leaving impact area unset.",
                ImpactAreaPrompt.Version, context.TopicGroup.Id);
        }

        return null;
    }

    /// <inheritdoc />
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

    /// <summary>
    /// Validates the model's choice against the closed set and returns the canonical vocabulary spelling
    /// (case/whitespace-insensitive), or <see langword="null"/> when the value is blank or off-list.
    /// </summary>
    private static string? Normalize(string? value, IReadOnlyList<string> impactAreas)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return impactAreas.FirstOrDefault(area => string.Equals(area, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ImpactAreaResult(
        [property: JsonPropertyName("impactArea")] string? ImpactArea,
        [property: JsonPropertyName("rationale")] string? Rationale);
}
