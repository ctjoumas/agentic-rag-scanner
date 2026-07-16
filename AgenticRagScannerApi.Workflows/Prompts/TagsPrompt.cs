using System.Text;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Prompts;

/// <summary>
/// System- and user-prompt builders for the Tags agent (Epic 8, story 8.3). It runs once per vetted
/// document and selects the applicable tags for THAT document. The approved tag vocabulary is injected
/// (loaded from Cosmos RegDocs at runtime) rather than hardcoded, so the set can be re-seeded without
/// a prompt change. Versioned via <see cref="Version"/> so eval runs can attribute output changes to
/// prompt changes (see <c>docs/prompt-management.md</c>).
/// </summary>
public static class TagsPrompt
{
    /// <summary>Prompt version - bump when the instructions change.</summary>
    public const string Version = "v3";

    /// <summary>
    /// Builds the system prompt: role, the approved tag set, and the multi-label rules. The JSON wrapper
    /// is enforced by Structured Outputs, so the prompt describes the choice, not the schema.
    /// </summary>
    public static string BuildSystemPrompt(string jurisdiction, IReadOnlyList<string> tags)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "You are a categorization assistant for an employment-taxes regulatory horizon-scanning system.");
        builder.AppendLine(
            $"A scan surfaced a regulatory update in the {jurisdiction} jurisdiction. Assign the applicable " +
            "tags for THIS regulatory document.");
        builder.AppendLine();
        builder.AppendLine("Rules:");
        builder.AppendLine(
            "- Choose tags ONLY from the approved list below. This is a controlled vocabulary - do not invent, " +
            "merge, reword, or abbreviate a tag. Return each chosen tag's text verbatim.");
        builder.AppendLine(
            "- This is multi-label: assign EVERY tag that genuinely applies to the document (zero, one, or " +
            "several). Do not force a tag when none fit, and do not pad the list with loosely-related tags - " +
            "precision matters.");
        builder.AppendLine(
            "- Base the decision on the substance of the update (what the change actually does), grounded in the " +
            "provided full text where available; fall back to the title/URL and relevance notes when it is not.");
        builder.AppendLine("- Do not repeat a tag. Order does not matter.");
        builder.AppendLine(
            "- Give a one-sentence rationale (max ~30 words) for the selection - recorded for observability, not shown to end users.");
        builder.AppendLine();
        builder.AppendLine("Approved tags (choose zero or more, verbatim):");
        foreach (var tag in tags)
        {
            builder.AppendLine($"- {tag}");
        }

        return builder.ToString();
    }

    /// <summary>Builds the user prompt: the topic group and the shared block of the group's vetted updates.</summary>
    public static string BuildUserPrompt(TopicGroupContext context, string updatesBlock)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"Topic group: {context.TopicGroup.Name}");
        builder.AppendLine($"Jurisdiction: {context.Run.Jurisdiction}");
        builder.AppendLine();
        builder.AppendLine(updatesBlock);
        builder.Append("Return every applicable tag for this regulatory document (verbatim from the approved list) with a one-sentence rationale.");
        return builder.ToString();
    }
}
