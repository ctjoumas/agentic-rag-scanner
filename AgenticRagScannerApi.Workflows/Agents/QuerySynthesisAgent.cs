using System.Text.Json;
using System.Text.Json.Serialization;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Configuration;
using AgenticRagScannerApi.Workflows.Prompts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Epic 3 (story 3.3) real implementation of <see cref="IQuerySynthesisAgent"/>: a MAF
/// <see cref="ChatClientAgent"/> over the shared Foundry model deployment (<see cref="IChatClient"/>).
/// It synthesizes focused search-query strings from the topic group's keyword OR-list and, on re-loops,
/// reads <see cref="SearchHistory"/> to rotate synonym coverage and avoid redundant queries. Output is
/// structured JSON with a bounded retry on invalid JSON; after the retry budget is exhausted it falls
/// back to a deterministic keyword query so the loop never stalls. It returns query strings only - the
/// Web Search agent (Epic 4) executes them against Bing.
/// </summary>
public sealed class QuerySynthesisAgent : IQuerySynthesisAgent
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IChatClient _chatClient;
    private readonly QuerySynthesisOptions _options;
    private readonly ILogger<QuerySynthesisAgent> _logger;

    public QuerySynthesisAgent(
        IChatClient chatClient,
        IOptions<QuerySynthesisOptions> options,
        ILogger<QuerySynthesisAgent> logger)
    {
        _chatClient = chatClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> SynthesizeAsync(TopicGroupContext context, CancellationToken cancellationToken = default)
    {
        var pass = context.LoopCount + 1;
        var systemPrompt = QuerySynthesisPrompt.BuildSystemPrompt(context.Run.Jurisdiction, _options.MaxQueries);
        var userPrompt = QuerySynthesisPrompt.BuildUserPrompt(context);

        var agent = new ChatClientAgent(_chatClient, new ChatClientAgentOptions
        {
            Name = "QuerySynthesis",
            ChatOptions = new ChatOptions
            {
                Instructions = systemPrompt,
                Temperature = _options.Temperature,
                ResponseFormat = ChatResponseFormat.Json,
            },
        });

        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            var message = attempt == 1
                ? userPrompt
                : userPrompt + "\n\nThe previous response was not valid JSON. Respond with JSON only, " +
                  "in exactly this shape: {\"queries\":[\"...\"]}.";

            var response = await agent.RunAsync(message, cancellationToken: cancellationToken);

            if (TryParseQueries(response.Text, _options.MaxQueries, out var queries))
            {
                _logger.LogInformation(
                    "QuerySynthesis ({PromptVersion}) for group '{GroupId}', pass {Pass}: {Count} query(ies) on attempt {Attempt}.",
                    QuerySynthesisPrompt.Version, context.TopicGroup.Id, pass, queries.Count, attempt);
                return queries;
            }

            _logger.LogWarning(
                "QuerySynthesis ({PromptVersion}) for group '{GroupId}', pass {Pass}: invalid JSON on attempt {Attempt}/{MaxAttempts}.",
                QuerySynthesisPrompt.Version, context.TopicGroup.Id, pass, attempt, _options.MaxAttempts);
        }

        var fallback = BuildFallbackQueries(context);
        _logger.LogWarning(
            "QuerySynthesis ({PromptVersion}) for group '{GroupId}', pass {Pass}: falling back to a deterministic query after {MaxAttempts} invalid attempt(s).",
            QuerySynthesisPrompt.Version, context.TopicGroup.Id, pass, _options.MaxAttempts);
        return fallback;
    }

    /// <summary>
    /// Parses the model's JSON (tolerating stray prose / code fences), then trims, drops blanks,
    /// de-duplicates case-insensitively, and caps the count. Returns false on any failure.
    /// </summary>
    private static bool TryParseQueries(string? text, int maxQueries, out IReadOnlyList<string> queries)
    {
        queries = [];
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<QueryListResult>(ExtractJsonObject(text), s_jsonOptions);
            if (parsed?.Queries is null)
            {
                return false;
            }

            var cleaned = parsed.Queries
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .Select(q => q.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxQueries)
                .ToList();

            if (cleaned.Count == 0)
            {
                return false;
            }

            queries = cleaned;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Extracts the first JSON object span from the text so wrapping prose/fences are ignored.</summary>
    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    /// <summary>Deterministic single-query fallback derived from the topic group's keywords.</summary>
    private static IReadOnlyList<string> BuildFallbackQueries(TopicGroupContext context)
    {
        var keywords = context.TopicGroup.Keywords;
        var primary = keywords.Count > 0 ? keywords[0] : context.TopicGroup.Name;
        return [$"{primary} {context.Run.Jurisdiction} update"];
    }

    private sealed record QueryListResult(
        [property: JsonPropertyName("queries")] IReadOnlyList<string>? Queries);
}
