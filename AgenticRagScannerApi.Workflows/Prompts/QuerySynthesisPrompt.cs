using System.Text;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Prompts;

/// <summary>
/// System- and user-prompt builders for the Query Synthesis agent (Epic 3, story 3.3). Prompt text is
/// externalized here and versioned via <see cref="Version"/> so eval runs can attribute output changes
/// to prompt changes. See <c>docs/prompt-management.md</c> for the convention.
/// </summary>
public static class QuerySynthesisPrompt
{
    /// <summary>Prompt version - bump when the instructions change.</summary>
    public const string Version = "v1";

    /// <summary>Builds the system prompt: role, rules, and the required JSON response shape.</summary>
    public static string BuildSystemPrompt(string jurisdiction, int maxQueries) =>
        $$"""
        You are a query-synthesis assistant for a regulatory horizon-scanning system.
        Turn a curated topic group into focused web-search queries that surface primary-source
        regulatory updates for the {{jurisdiction}} jurisdiction.

        Rules:
        - Produce between 1 and {{maxQueries}} distinct, focused queries; you decide how many.
        - Target authoritative primary sources (government, regulators, legislation, official guidance).
        - Rotate synonym and alias coverage across the topic group's keyword OR-list. Do NOT repeat
          queries already tried in earlier passes - cover untested synonyms and gaps instead.
        - Keep each query concise, like a search box entry: no boolean operators, quotes, or site: filters.
        - Prefer recency-oriented phrasing (for example "update" or "change") where it helps.

        Respond with JSON only, in exactly this shape:
        {"queries":["first query","second query"]}
        Output JSON only - no prose, no markdown, no code fences.
        """;

    /// <summary>Builds the user prompt: the topic group, pass number, keyword OR-list, and prior queries.</summary>
    public static string BuildUserPrompt(TopicGroupContext context)
    {
        var pass = context.LoopCount + 1;
        var builder = new StringBuilder();

        builder.AppendLine($"Topic group: {context.TopicGroup.Name}");
        builder.AppendLine($"Jurisdiction: {context.Run.Jurisdiction}");
        builder.AppendLine($"Pass: {pass} of up to {context.TopicGroup.MaxLoops}");

        builder.AppendLine("Keyword / synonym OR-list:");
        foreach (var keyword in context.TopicGroup.Keywords)
        {
            builder.AppendLine($"- {keyword}");
        }

        var priorQueries = context.History.Queries.ToList();
        builder.AppendLine("Queries already tried (avoid repeating; rotate to untested synonyms/gaps):");
        if (priorQueries.Count == 0)
        {
            builder.AppendLine("- (none yet - this is the first pass)");
        }
        else
        {
            foreach (var query in priorQueries)
            {
                builder.AppendLine($"- {query}");
            }
        }

        builder.Append("Return JSON only.");
        return builder.ToString();
    }
}
