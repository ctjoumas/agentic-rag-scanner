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
    public const string Version = "v4";

    /// <summary>
    /// Builds the system prompt: role and rules. The response shape is enforced by Structured Outputs
    /// (a JSON schema), so the prompt describes the query, not the JSON wrapper.
    /// </summary>
    public static string BuildSystemPrompt(string jurisdiction) =>
        $$"""
        You are a query-synthesis assistant for a regulatory horizon-scanning system.
        Turn a curated topic group into a single web-search query that surfaces primary-source
        regulatory updates for the {{jurisdiction}} jurisdiction.

        A topic group is ONE coherent theme expressed through many surface forms - synonyms, aliases,
        and acronyms that co-occur on the same authoritative pages (for example NIC / National
        Insurance Contributions, ITEPA 2003, PAYE, CIS, salary sacrifice all describe payroll
        withholding). Treat the group as that single theme, not as separate unrelated topics.

        Rules:
        - Produce exactly one query that represents the WHOLE theme of the group. Do not drop part of
          the group - name the most representative terms naturally so the search covers the theme.
        - Write a concise natural-language query (~10-25 words), like a search-box entry. Do NOT use
          boolean operators (AND/OR), quotes, or site: filters - web grounding ignores them and a long
          OR-concatenation degrades ranking.
        - You need not list every phrase verbatim: name the handful of most salient/representative
          terms; the retriever generalizes from them to the rest of the cluster.
        - Preserve named entities and acronyms exactly (for example ITEPA 2003, CIS, NIC, PAYE). Where
          it aids recall, expand a key acronym to its full form alongside the acronym.
        - Target authoritative primary sources (government, regulators, legislation, official guidance)
          and prefer recency-oriented phrasing (for example "update", "change", "2026") where it helps.
        - This runs in an agentic loop. On the FIRST pass, write a broad query for the whole theme. On
          LATER passes, do not repeat an earlier query: use the prior queries and the reviewer's notes
          to zoom into the facet that was under-covered, while staying within the same theme.
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
        builder.AppendLine("Queries already tried (do not repeat; cover the same theme from an angle that fills a gap):");
        if (priorQueries.Count == 0)
        {
            builder.AppendLine("- (none yet - this is the first pass; write a broad query for the whole theme)");
        }
        else
        {
            foreach (var query in priorQueries)
            {
                builder.AppendLine($"- {query}");
            }
        }

        var priorReviews = context.History.Reviews.ToList();
        if (priorReviews.Count > 0)
        {
            builder.AppendLine("Reviewer notes from earlier passes (what was found and what is still missing - steer toward the gaps):");
            foreach (var review in priorReviews)
            {
                builder.AppendLine($"- {review}");
            }
        }

        builder.Append("Return the single best query for this pass.");
        return builder.ToString();
    }
}
