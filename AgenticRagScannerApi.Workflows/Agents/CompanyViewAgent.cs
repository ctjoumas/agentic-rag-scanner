using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Common;
using AgenticRagScannerApi.Workflows.Configuration;
using AgenticRagScannerApi.Workflows.Prompts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Epic 8 (story 8.5) real implementation of <see cref="ICompanyViewAgent"/>: a MAF
/// <see cref="ChatClientAgent"/> over the shared Foundry model deployment (<see cref="IChatClient"/>).
/// In a SINGLE Structured Output call it produces ONE <see cref="CompanyViewRecord"/> for a single
/// vetted document - both a neutral <c>SummaryOfUpdate</c> of what changed AND the practitioner-style
/// <c>CompanyView</c> advice - grounded on that document's <em>full text</em>. Prior Company View exemplars
/// for the run's jurisdiction are passed in by the finalize step (fetched once per group) and steer the
/// CompanyView only; the summary is grounded purely in the full text and does not use them. The objective
/// fields (jurisdiction, tags, impact area, dates, supporting reference) are filled deterministically from
/// the item so they stay grounded. A failed/empty call still returns a record populated with the objective fields.
/// </summary>
public sealed class CompanyViewAgent : ICompanyViewAgent
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IChatClient _chatClient;
    private readonly CompanyViewOptions _options;
    private readonly ILogger<CompanyViewAgent> _logger;

    public CompanyViewAgent(
        IChatClient chatClient,
        IOptions<CompanyViewOptions> options,
        ILogger<CompanyViewAgent> logger)
    {
        _chatClient = chatClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<CompanyViewRecord?> GenerateAsync(
        ResultItem item,
        string? fullText,
        string? impactArea,
        IReadOnlyList<string> tags,
        IReadOnlyList<CompanyViewRecord> priorViews,
        TopicGroupContext context,
        CancellationToken cancellationToken = default)
        => GenerateCoreAsync(
            [item],
            new Dictionary<string, string?> { [item.Id] = fullText },
            impactArea,
            tags,
            priorViews,
            context,
            cancellationToken);

    /// <summary>
    /// Shared body for both the group and single-item overloads: builds the deterministic objective
    /// fields, grounds the model on the supplied items' full text and the <paramref name="exemplars"/>
    /// (already fetched/capped by the caller), and overlays the model's judgement fields. A failed/empty
    /// model call still returns a record populated with the objective fields.
    /// </summary>
    private async Task<CompanyViewRecord?> GenerateCoreAsync(
        IReadOnlyList<ResultItem> items,
        IReadOnlyDictionary<string, string?> fullTextByItemId,
        string? impactArea,
        IReadOnlyList<string> tags,
        IReadOnlyList<CompanyViewRecord> exemplars,
        TopicGroupContext context,
        CancellationToken cancellationToken)
    {
        var jurisdiction = context.Run.Jurisdiction;

        // The objective fields are aggregated deterministically from the carried items and the
        // categorization (impact area + tags) so they stay grounded.
        var record = BuildBaseRecord(items, jurisdiction, impactArea, tags);

        var updatesBlock = AggregateContextBuilder.BuildUpdatesBlock(items, fullTextByItemId);
        var systemPrompt = CompanyViewPrompt.BuildSystemPrompt(jurisdiction);
        var userPrompt = CompanyViewPrompt.BuildUserPrompt(context, updatesBlock, impactArea, tags, exemplars);

        var agent = new ChatClientAgent(_chatClient, new ChatClientAgentOptions
        {
            Name = "CompanyView",
            ChatOptions = new ChatOptions
            {
                Instructions = systemPrompt,
                Temperature = _options.Temperature,
            },
        });

        try
        {
            var response = await agent.RunAsync<CompanyViewLlmResult>(
                userPrompt,
                serializerOptions: s_jsonOptions,
                cancellationToken: cancellationToken);

            ApplyLlmResult(record, response.Result);

            if (string.IsNullOrWhiteSpace(record.CompanyView))
            {
                _logger.LogWarning(
                    "CompanyView ({PromptVersion}) for group '{GroupId}': model returned an empty view; record carries aggregated fields only.",
                    CompanyViewPrompt.Version, context.TopicGroup.Id);
            }
            else
            {
                _logger.LogInformation(
                    "CompanyView ({PromptVersion}) for group '{GroupId}': record produced from {Items} update(s) using {Exemplars} exemplar(s).",
                    CompanyViewPrompt.Version, context.TopicGroup.Id, items.Count, exemplars.Count);
            }
        }
        catch (Exception ex) when (ChatFailure.IsDegradable(ex, cancellationToken))
        {
            _logger.LogWarning(
                ex,
                "CompanyView ({PromptVersion}) for group '{GroupId}': generation call failed; record carries aggregated fields only.",
                CompanyViewPrompt.Version, context.TopicGroup.Id);
        }

        return record;
    }

    /// <summary>
    /// Builds the objective fields of the record: the dates and supporting references are aggregated
    /// deterministically from the carried items, while the impact area and tags are the group-level
    /// categorization computed once over all items' full text.
    /// </summary>
    private static CompanyViewRecord BuildBaseRecord(
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

        return new CompanyViewRecord
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
    private static void ApplyLlmResult(CompanyViewRecord record, CompanyViewLlmResult? result)
    {
        if (result is null)
        {
            return;
        }

        record.TitleOfUpdate = Trim(result.TitleOfUpdate);
        record.SummaryOfUpdate = Trim(result.SummaryOfUpdate);
        record.CompanyView = Trim(result.CompanyView);
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

    private sealed record CompanyViewLlmResult(
        [property: JsonPropertyName("titleOfUpdate")] string? TitleOfUpdate,
        [property: JsonPropertyName("summaryOfUpdate")] string? SummaryOfUpdate,
        [property: JsonPropertyName("companyView")] string? CompanyView,
        [property: JsonPropertyName("levelOfAuthority")] string? LevelOfAuthority,
        [property: JsonPropertyName("statusOfChange")] string? StatusOfChange,
        [property: JsonPropertyName("regulator")] string? Regulator);
}
