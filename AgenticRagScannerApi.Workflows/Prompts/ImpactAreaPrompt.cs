using System.Text;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Prompts;

/// <summary>
/// System- and user-prompt builders for the Impact Area agent (Epic 8, story 8.2). It runs ONCE per
/// topic group and assigns the single best impact area across ALL the group's vetted regulatory updates
/// (not per item). The approved impact-area vocabulary is injected (loaded from Cosmos RegDocs at
/// runtime) rather than hardcoded, so the closed set can be re-seeded without a prompt change. Versioned
/// via <see cref="Version"/> so eval runs can attribute output changes to prompt changes (see
/// <c>docs/prompt-management.md</c>).
/// </summary>
public static class ImpactAreaPrompt
{
    /// <summary>Prompt version - bump when the instructions change.</summary>
    public const string Version = "v2";

    /// <summary>
    /// Builds the system prompt: role, the closed impact-area set, and the single-label rule. The JSON
    /// wrapper is enforced by Structured Outputs, so the prompt describes the choice, not the schema.
    /// </summary>
    public static string BuildSystemPrompt(string jurisdiction, IReadOnlyList<string> impactAreas)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "You are a categorization assistant for an employment-taxes regulatory horizon-scanning system.");
        builder.AppendLine(
            $"A scan surfaced one or more regulatory updates for a single topic group in the {jurisdiction} " +
            "jurisdiction. Assign the SINGLE most appropriate impact area for the topic group as a whole, " +
            "considering all of the updates together.");
        builder.AppendLine();
        builder.AppendLine("Rules:");
        builder.AppendLine(
            "- Choose EXACTLY ONE impact area, and ONLY from the approved list below. This is a closed set - " +
            "do not invent, merge, reword, or abbreviate an option. Return the chosen option's text verbatim.");
        builder.AppendLine(
            "- Base the decision on the substance of the updates (what the changes actually do), grounded in the " +
            "provided full text where available; fall back to the titles/URLs and relevance notes when it is not.");
        builder.AppendLine(
            "- If the updates span several areas, pick the one that best captures the group's primary effect. " +
            "Always return one - never leave it blank and never return more than one.");
        builder.AppendLine(
            "- Give a one-sentence rationale (max ~30 words) for the choice - recorded for observability, not shown to end users.");
        builder.AppendLine();
        builder.AppendLine("Approved impact areas (choose exactly one, verbatim):");
        foreach (var impactArea in impactAreas)
        {
            builder.AppendLine($"- {impactArea}");
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
        builder.Append("Return the single best impact area for this topic group (verbatim from the approved list) with a one-sentence rationale.");
        return builder.ToString();
    }
}
