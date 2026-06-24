using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Pipeline;
using AgenticRagScannerApi.Workflows.Prompts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Epic 6 (story 6.1) real implementation of <see cref="IRelevanceEvalAgent"/>: a MAF
/// <see cref="ChatClientAgent"/> over the shared Foundry model deployment (<see cref="IChatClient"/>).
/// It makes a single full-text call that classifies every current-pass document RELEVANT / BORDERLINE /
/// NOT_RELEVANT, extracts effective-date-aware fields (publication vs effective vs tax-year applicability)
/// with a <see cref="DateConfidence"/>, and emits a loop <see cref="LoopDecision"/> plus a steer
/// (<c>ThoughtProcess</c>, story 6.5) for the next query-synthesis pass. Output uses Structured Outputs -
/// a strict JSON schema derived from <see cref="EvalResult"/> - so a single call returns verdicts and dates
/// well-formed (the strict schema guarantees conformance, so there is nothing to retry). If the call
/// genuinely fails (transport error or refusal), a conservative fallback marks every document BORDERLINE
/// (never dropped) and RETRY, so the loop never stalls and no candidate is silently discarded.
/// </summary>
public sealed class RelevanceEvalAgent : IRelevanceEvalAgent
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Low temperature so verdicts and date extraction stay deterministic and grounded.</summary>
    private const float Temperature = 0.1f;

    /// <summary>
    /// Per-document full-text budget (characters) fed to the model. Long documents are head-truncated to
    /// keep the multi-document prompt within the context window; a regulatory page's head carries the
    /// title, publication/effective dates, and substance, so truncating the tail is acceptable.
    /// </summary>
    private const int MaxCharsPerDocument = 24000;

    private readonly IChatClient _chatClient;
    private readonly ILogger<RelevanceEvalAgent> _logger;

    public RelevanceEvalAgent(IChatClient chatClient, ILogger<RelevanceEvalAgent> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<ReviewDecision> EvaluateAsync(
        TopicGroupContext context,
        IReadOnlyList<FetchedDocument> documents,
        CancellationToken cancellationToken = default)
    {
        var pass = context.LoopCount;

        if (documents.Count == 0)
        {
            _logger.LogInformation(
                "RelevanceEval ({PromptVersion}) for group '{GroupId}', pass {Pass}: no documents to evaluate; finalizing.",
                RelevanceEvalPrompt.Version, context.TopicGroup.Id, pass);

            return new ReviewDecision
            {
                ThoughtProcess = "No documents were retrieved this pass. Missing facets: none identifiable. Weak evidence: none.",
                Decision = LoopDecision.Finalize,
                Items = [],
            };
        }

        var systemPrompt = RelevanceEvalPrompt.BuildSystemPrompt(
            context.TopicGroup.Name,
            context.Run.Jurisdiction,
            FormatAsOf(context));
        var userPrompt = RelevanceEvalPrompt.BuildUserPrompt(context, documents, MaxCharsPerDocument);

        var agent = new ChatClientAgent(_chatClient, new ChatClientAgentOptions
        {
            Name = "RelevanceEval",
            ChatOptions = new ChatOptions
            {
                Instructions = systemPrompt,
                Temperature = Temperature,
            },
        });

        // A single Structured Outputs call: the strict JSON schema guarantees a schema-conformant result,
        // so there is nothing to retry. The only fallback is for a genuine call failure (transport error or
        // a refusal) - in that case we carry every document forward as BORDERLINE (never silently drop a
        // candidate) and RETRY, because a false negative is the costly error in a compliance context.
        try
        {
            var response = await agent.RunAsync<EvalResult>(
                userPrompt,
                serializerOptions: s_jsonOptions,
                cancellationToken: cancellationToken);

            if (response.Result?.Items is not null)
            {
                var decision = MapDecision(response.Result, documents.Count);
                _logger.LogInformation(
                    "RelevanceEval ({PromptVersion}) for group '{GroupId}', pass {Pass}: {Verdicts} verdict(s); decision {Decision}.",
                    RelevanceEvalPrompt.Version, context.TopicGroup.Id, pass, decision.Items.Count, decision.Decision);
                return decision;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogWarning(
                ex,
                "RelevanceEval ({PromptVersion}) for group '{GroupId}', pass {Pass}: eval call failed.",
                RelevanceEvalPrompt.Version, context.TopicGroup.Id, pass);
        }

        _logger.LogWarning(
            "RelevanceEval ({PromptVersion}) for group '{GroupId}', pass {Pass}: using conservative BORDERLINE/RETRY fallback.",
            RelevanceEvalPrompt.Version, context.TopicGroup.Id, pass);
        return BuildFallbackDecision(documents.Count);
    }

    /// <summary>
    /// Maps the model's raw structured result (<see cref="EvalResult"/>) into the Core
    /// <see cref="ReviewDecision"/>. We deliberately keep a separate LLM-facing DTO + this mapping rather
    /// than binding <c>RunAsync&lt;ReviewDecision&gt;</c> directly, for three reasons:
    ///  1. Dates: the structured-output JSON schema is derived from the bound type. <see cref="DateOnly"/>
    ///     does not have a reliable, universally-honored schema representation across providers, so binding
    ///     <see cref="ReviewDecision"/> (whose date fields are <see cref="DateOnly"/>?) risks a malformed
    ///     schema or a refusal. Instead the wire DTO takes dates as plain strings and we parse them here
    ///     (<see cref="ParseDate"/>), tolerating blanks/partials by yielding <see langword="null"/>.
    ///  2. Enum tolerance: the wire DTO takes verdict/decision/confidence as strings so we accept casing and
    ///     spelling variants (RELEVANT / Relevant / NOT_RELEVANT / "not relevant") without a refusal.
    ///  3. Recall safety: this is where we fill any index the model omitted as BORDERLINE (below) so a
    ///     document is never silently dropped - a domain concern that doesn't belong on the wire contract.
    /// If those tradeoffs are ever accepted, this could collapse to a direct bind (add a
    /// <c>JsonStringEnumConverter</c> and switch the date fields to strings on the Core type).
    /// </summary>
    private static ReviewDecision MapDecision(EvalResult result, int documentCount)
    {
        var items = new List<ItemVerdict>(documentCount);
        var seen = new HashSet<int>();

        foreach (var item in result.Items!)
        {
            if (item.Index < 0 || item.Index >= documentCount || !seen.Add(item.Index))
            {
                continue;
            }

            items.Add(new ItemVerdict
            {
                Index = item.Index,
                Verdict = ParseVerdict(item.Verdict),
                Rationale = string.IsNullOrWhiteSpace(item.Rationale) ? null : item.Rationale.Trim(),
                PublicationDate = ParseDate(item.PublicationDate),
                EffectiveDate = ParseDate(item.EffectiveDate),
                AppliesFrom = ParseDate(item.AppliesFrom),
                AppliesTo = ParseDate(item.AppliesTo),
                DateConfidence = ParseDateConfidence(item.DateConfidence),
            });
        }

        // Any index the model omitted is treated conservatively as BORDERLINE (carried, flagged).
        for (var i = 0; i < documentCount; i++)
        {
            if (seen.Add(i))
            {
                items.Add(new ItemVerdict
                {
                    Index = i,
                    Verdict = Verdict.Borderline,
                    Rationale = "Eval agent did not return a verdict for this document; carried forward as borderline.",
                    DateConfidence = DateConfidence.Unknown,
                });
            }
        }

        items.Sort((a, b) => a.Index.CompareTo(b.Index));

        return new ReviewDecision
        {
            ThoughtProcess = string.IsNullOrWhiteSpace(result.ThoughtProcess)
                ? "Missing facets: none. Weak evidence: none."
                : result.ThoughtProcess.Trim(),
            Decision = ParseDecision(result.Decision),
            Items = items,
        };
    }

    /// <summary>Conservative fallback: carry every document forward as BORDERLINE and retry.</summary>
    private static ReviewDecision BuildFallbackDecision(int documentCount)
    {
        var items = new List<ItemVerdict>(documentCount);
        for (var i = 0; i < documentCount; i++)
        {
            items.Add(new ItemVerdict
            {
                Index = i,
                Verdict = Verdict.Borderline,
                Rationale = "Relevance eval could not be completed; carried forward as borderline for safety.",
                DateConfidence = DateConfidence.Unknown,
            });
        }

        return new ReviewDecision
        {
            ThoughtProcess = "Eval call failed; no facet analysis available. Missing facets: unknown. Weak evidence: unknown.",
            Decision = LoopDecision.Retry,
            Items = items,
        };
    }

    private static Verdict ParseVerdict(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "RELEVANT" => Verdict.Relevant,
        "NOT_RELEVANT" or "NOTRELEVANT" or "NOT RELEVANT" => Verdict.NotRelevant,
        _ => Verdict.Borderline,
    };

    private static LoopDecision ParseDecision(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "FINALIZE" or "FINALISE" or "STOP" => LoopDecision.Finalize,
        _ => LoopDecision.Retry,
    };

    private static DateConfidence ParseDateConfidence(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "HIGH" => DateConfidence.High,
        "MEDIUM" => DateConfidence.Medium,
        "LOW" => DateConfidence.Low,
        _ => DateConfidence.Unknown,
    };

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static string FormatAsOf(TopicGroupContext context) =>
        context.Run.AsOfDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        ?? context.Run.StartedAtUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record EvalResult(
        [property: JsonPropertyName("thoughtProcess")] string? ThoughtProcess,
        [property: JsonPropertyName("decision")] string? Decision,
        [property: JsonPropertyName("items")] IReadOnlyList<EvalItem>? Items);

    private sealed record EvalItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("verdict")] string? Verdict,
        [property: JsonPropertyName("rationale")] string? Rationale,
        [property: JsonPropertyName("publicationDate")] string? PublicationDate,
        [property: JsonPropertyName("effectiveDate")] string? EffectiveDate,
        [property: JsonPropertyName("appliesFrom")] string? AppliesFrom,
        [property: JsonPropertyName("appliesTo")] string? AppliesTo,
        [property: JsonPropertyName("dateConfidence")] string? DateConfidence);
}
