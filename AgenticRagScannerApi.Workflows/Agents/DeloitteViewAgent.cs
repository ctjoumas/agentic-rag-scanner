using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.DeloitteView;
using AgenticRagScannerApi.Workflows.Prompts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Epic 8 (story 8.5) real implementation of <see cref="IDeloitteViewAgent"/>: a MAF
/// <see cref="ChatClientAgent"/> over the shared Foundry model deployment (<see cref="IChatClient"/>).
/// In a SINGLE Structured-Outputs call it produces ONE consolidated <see cref="DeloitteViewRecord"/> for
/// a topic group - both a neutral <c>SummaryOfUpdate</c> of what changed AND the practitioner-style
/// <c>DeloitteView</c> advice - grounded on the <em>full text</em> of the group's carried updates. Prior
/// Deloitte View records for the run's jurisdiction (<see cref="IPriorDeloitteViewSource"/>) are injected
/// as house-style exemplars that steer the DeloitteView only; the summary is grounded purely in the full
/// text and does not use them. The objective fields (jurisdiction, tags, impact areas, dates, supporting
/// references) are filled deterministically from the carried items so they stay grounded. A failed/empty
/// call still returns a record populated with the objective fields.
/// </summary>
public sealed class DeloitteViewAgent : IDeloitteViewAgent
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Low-ish temperature so the advice stays grounded and on-style without being wooden.</summary>
    private const float Temperature = 0.3f;

    /// <summary>Cap on prior-view exemplars fed to the model, to bound tokens.</summary>
    private const int MaxExemplars = 5;

    private readonly IChatClient _chatClient;
    private readonly IPriorDeloitteViewSource _priorViews;
    private readonly ILogger<DeloitteViewAgent> _logger;

    public DeloitteViewAgent(
        IChatClient chatClient,
        IPriorDeloitteViewSource priorViews,
        ILogger<DeloitteViewAgent> logger)
    {
        _chatClient = chatClient;
        _priorViews = priorViews;
        _logger = logger;
    }

    public async Task<DeloitteViewRecord?> GenerateAsync(
        IReadOnlyList<ResultItem> items,
        IReadOnlyDictionary<string, string?> fullTextByItemId,
        string? impactArea,
        IReadOnlyList<string> tags,
        TopicGroupContext context,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            _logger.LogInformation(
                "DeloitteView ({PromptVersion}) for group '{GroupId}': no carried items; skipping.",
                DeloitteViewPrompt.Version, context.TopicGroup.Id);
            return null;
        }

        var jurisdiction = context.Run.Jurisdiction;
        var priorViews = await _priorViews.GetByJurisdictionAsync(jurisdiction, cancellationToken);
        var exemplars = priorViews.Count > MaxExemplars ? priorViews.Take(MaxExemplars).ToList() : priorViews;

        // The objective fields are aggregated deterministically from the carried items and the group-level
        // categorization (impact area + tags computed once over all items' full text) so they stay grounded.
        var record = BuildBaseRecord(items, jurisdiction, impactArea, tags);

        var updatesBlock = AggregateContextBuilder.BuildUpdatesBlock(items, fullTextByItemId);
        var systemPrompt = DeloitteViewPrompt.BuildSystemPrompt(jurisdiction);
        var userPrompt = DeloitteViewPrompt.BuildUserPrompt(context, updatesBlock, impactArea, tags, exemplars);

        var agent = new ChatClientAgent(_chatClient, new ChatClientAgentOptions
        {
            Name = "DeloitteView",
            ChatOptions = new ChatOptions
            {
                Instructions = systemPrompt,
                Temperature = Temperature,
            },
        });

        try
        {
            var response = await agent.RunAsync<DeloitteViewLlmResult>(
                userPrompt,
                serializerOptions: s_jsonOptions,
                cancellationToken: cancellationToken);

            ApplyLlmResult(record, response.Result);

            if (string.IsNullOrWhiteSpace(record.DeloitteView))
            {
                _logger.LogWarning(
                    "DeloitteView ({PromptVersion}) for group '{GroupId}': model returned an empty view; record carries aggregated fields only.",
                    DeloitteViewPrompt.Version, context.TopicGroup.Id);
            }
            else
            {
                _logger.LogInformation(
                    "DeloitteView ({PromptVersion}) for group '{GroupId}': record produced from {Items} update(s) using {Exemplars} exemplar(s).",
                    DeloitteViewPrompt.Version, context.TopicGroup.Id, items.Count, exemplars.Count);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogWarning(
                ex,
                "DeloitteView ({PromptVersion}) for group '{GroupId}': generation call failed; record carries aggregated fields only.",
                DeloitteViewPrompt.Version, context.TopicGroup.Id);
        }

        return record;
    }

    /// <summary>
    /// Builds the objective fields of the record: the dates and supporting references are aggregated
    /// deterministically from the carried items, while the impact area and tags are the group-level
    /// categorization computed once over all items' full text.
    /// </summary>
    private static DeloitteViewRecord BuildBaseRecord(
        IReadOnlyList<ResultItem> items,
        string jurisdiction,
        string? impactArea,
        IReadOnlyList<string> tags)
    {
        var sourceUrls = items
            .SelectMany(i => i.SourceUrls)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DeloitteViewRecord
        {
            Jurisdiction = jurisdiction,
            ImpactArea = string.IsNullOrWhiteSpace(impactArea) ? null : impactArea.Trim(),
            Tags = tags,
            AnnouncementDate = FormatEarliest(items.Select(i => i.PublicationDate)),
            EffectiveDateOfChange = FormatEarliest(items.Select(i => i.EffectiveDate)),
            SupportingReference = sourceUrls.Count > 0 ? string.Join(" | ", sourceUrls) : null,
        };
    }

    /// <summary>Overlays the model-synthesized judgement fields onto the aggregated record.</summary>
    private static void ApplyLlmResult(DeloitteViewRecord record, DeloitteViewLlmResult? result)
    {
        if (result is null)
        {
            return;
        }

        record.TitleOfUpdate = Trim(result.TitleOfUpdate);
        record.SummaryOfUpdate = Trim(result.SummaryOfUpdate);
        record.DeloitteView = Trim(result.DeloitteView);
        record.LevelOfAuthority = Trim(result.LevelOfAuthority);
        record.StatusOfChange = Trim(result.StatusOfChange);
        record.Regulator = Trim(result.Regulator);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Formats the earliest known date in a sequence as yyyy-MM-dd, or null when none are set.</summary>
    private static string? FormatEarliest(IEnumerable<DateOnly?> dates)
    {
        DateOnly? earliest = null;
        foreach (var date in dates)
        {
            if (date is { } value && (earliest is null || value < earliest))
            {
                earliest = value;
            }
        }

        return earliest?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private sealed record DeloitteViewLlmResult(
        [property: JsonPropertyName("titleOfUpdate")] string? TitleOfUpdate,
        [property: JsonPropertyName("summaryOfUpdate")] string? SummaryOfUpdate,
        [property: JsonPropertyName("deloitteView")] string? DeloitteView,
        [property: JsonPropertyName("levelOfAuthority")] string? LevelOfAuthority,
        [property: JsonPropertyName("statusOfChange")] string? StatusOfChange,
        [property: JsonPropertyName("regulator")] string? Regulator);
}
