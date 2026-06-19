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
    public const string Version = "v3";

    /// <summary>
    /// Builds the system prompt: role and rules. The response shape is enforced by Structured Outputs
    /// (a JSON schema), so the prompt describes the query, not the JSON wrapper.
    /// </summary>
    public static string BuildSystemPrompt(string jurisdiction) =>
        $$"""
        You are a query-synthesis assistant for a regulatory horizon-scanning system.
        Turn a curated topic group into a single focused web-search query that surfaces primary-source
        regulatory updates for the {{jurisdiction}} jurisdiction.

        Rules:
        - Produce exactly one query - the single best query for this pass.
        - This runs in an agentic loop: if a pass underperforms, a later pass synthesizes another
          query. So do NOT try to cover everything at once - pick the highest-value angle now.
        - Target authoritative primary sources (government, regulators, legislation, official guidance).
        - Rotate synonym and alias coverage across the topic group's keyword OR-list. Do NOT repeat a
          query already tried in earlier passes - cover an untested synonym or gap instead.
        - Keep the query concise, like a search box entry: no boolean operators, quotes, or site: filters.
        - Prefer recency-oriented phrasing (for example "update" or "change") where it helps.
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

        builder.Append("Return the single best query for this pass.");
        return builder.ToString();
    }
}
