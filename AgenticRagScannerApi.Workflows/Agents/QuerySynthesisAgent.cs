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
/// It synthesizes a single focused search query from the topic group's keyword OR-list and, on re-loops,
/// reads <see cref="SearchHistory"/> to rotate synonym coverage and avoid a redundant query. Breadth
/// comes from the agentic loop (one query per pass), not from emitting many queries at once. Output uses
/// Structured Outputs - a strict JSON schema derived from <see cref="QueryResult"/> - so the model returns
/// a well-formed query without ad-hoc JSON parsing; a deterministic keyword query is used as a fallback
/// if the model refuses or returns a blank result, so the loop never stalls. It returns a query string
/// only - the Web Search agent (Epic 4) executes it against Bing.
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

    public async Task<string> SynthesizeAsync(TopicGroupContext context, CancellationToken cancellationToken = default)
    {
        var pass = context.LoopCount + 1;
        var systemPrompt = QuerySynthesisPrompt.BuildSystemPrompt(context.Run.Jurisdiction);
        var userPrompt = QuerySynthesisPrompt.BuildUserPrompt(context);

        var agent = new ChatClientAgent(_chatClient, new ChatClientAgentOptions
        {
            Name = "QuerySynthesis",
            ChatOptions = new ChatOptions
            {
                Instructions = systemPrompt,
                Temperature = _options.Temperature,
            },
        });

        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            var query = await TrySynthesizeOnceAsync(agent, userPrompt, cancellationToken);
            if (!string.IsNullOrWhiteSpace(query))
            {
                _logger.LogInformation(
                    "QuerySynthesis ({PromptVersion}) for group '{GroupId}', pass {Pass}: query synthesized on attempt {Attempt}.",
                    QuerySynthesisPrompt.Version, context.TopicGroup.Id, pass, attempt);
                return query;
            }

            _logger.LogWarning(
                "QuerySynthesis ({PromptVersion}) for group '{GroupId}', pass {Pass}: no usable query on attempt {Attempt}/{MaxAttempts}.",
                QuerySynthesisPrompt.Version, context.TopicGroup.Id, pass, attempt, _options.MaxAttempts);
        }

        var fallback = BuildFallbackQuery(context);
        _logger.LogWarning(
            "QuerySynthesis ({PromptVersion}) for group '{GroupId}', pass {Pass}: falling back to a deterministic query after {MaxAttempts} attempt(s).",
            QuerySynthesisPrompt.Version, context.TopicGroup.Id, pass, _options.MaxAttempts);
        return fallback;
    }

    /// <summary>
    /// Calls the model once using Structured Outputs (a strict JSON schema generated from
    /// <see cref="QueryResult"/>) and returns the trimmed query, or <see langword="null"/> when the model
    /// refuses or returns a blank / unparseable result.
    /// </summary>
    private async Task<string?> TrySynthesizeOnceAsync(ChatClientAgent agent, string userPrompt, CancellationToken cancellationToken)
    {
        try
        {
            var response = await agent.RunAsync<QueryResult>(
                userPrompt,
                serializerOptions: s_jsonOptions,
                cancellationToken: cancellationToken);

            var query = response.Result.Query?.Trim();
            return string.IsNullOrWhiteSpace(query) ? null : query;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Deterministic single-query fallback derived from the topic group's keywords.</summary>
    private static string BuildFallbackQuery(TopicGroupContext context)
    {
        var keywords = context.TopicGroup.Keywords;
        var primary = keywords.Count > 0 ? keywords[0] : context.TopicGroup.Name;
        return $"{primary} {context.Run.Jurisdiction} update";
    }

    private sealed record QueryResult(
        [property: JsonPropertyName("query")] string? Query);
}
