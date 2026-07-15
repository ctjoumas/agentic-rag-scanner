using System.Text;
using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Prompts;

/// <summary>
/// System- and user-prompt builders for the Company View agent (Epic 8, story 8.5). In a single call
/// the agent produces ONE record per vetted document - both a neutral <c>SummaryOfUpdate</c> and the
/// practitioner-style <c>CompanyView</c> - grounded on the <em>full text</em> of that regulatory document
/// (plus its impact area and tags). Prior Company View records (passed in by the finalize step, shaped like
/// the historical CSV) are injected as house-style exemplars that steer the CompanyView only; the summary
/// does not use them. Versioned via <see cref="Version"/> so eval runs can attribute output changes to prompt
/// changes (see <c>docs/prompt-management.md</c>).
/// </summary>
public static class CompanyViewPrompt
{
    /// <summary>Prompt version - bump when the instructions change.</summary>
    public const string Version = "v6";

    /// <summary>Per-exemplar character budget so a few long prior views do not blow the context window.</summary>
    private const int MaxCharsPerExemplarField = 1200;

    /// <summary>
    /// Builds the system prompt: role, the fields to synthesize, tone, and grounding rules. The response
    /// shape is enforced by Structured Outputs, so the prompt describes the fields, not the JSON wrapper.
    /// </summary>
    public static string BuildSystemPrompt(string jurisdiction)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "You are an employment-taxes practitioner producing a \"Company View\" record for a single " +
            "regulatory document.");
        builder.AppendLine(
            $"A scan surfaced a regulatory update in the {jurisdiction} jurisdiction. Produce ONE record for " +
            "THIS document.");
        builder.AppendLine();
        builder.AppendLine("Produce these fields:");
        builder.AppendLine("- titleOfUpdate: a concise headline capturing the theme of this document.");
        builder.AppendLine(
            "- summaryOfUpdate: a professional summary of WHAT CHANGED in this document (a few sentences).");
        builder.AppendLine(
            "- companyView: concise, practical client advice - what the changes mean for employers/clients and the " +
            "concrete actions they should consider. This is the centrepiece.");
        builder.AppendLine(
            "- levelOfAuthority: the authority of the underlying source(s) (e.g. legislation, regulator guidance, " +
            "consultation), if discernible; otherwise leave empty.");
        builder.AppendLine(
            "- statusOfChange: whether the change is in force, effective from a date, or only proposed/consultation, " +
            "if discernible; otherwise leave empty.");
        builder.AppendLine(
            "- regulator: the responsible authority/regulator (e.g. HMRC), if discernible; otherwise leave empty.");
        builder.AppendLine();
        builder.AppendLine("Rules:");
        builder.AppendLine(
            "- Write the companyView in the house style and tone of the prior Company View records provided as " +
            "examples - measured, advisory, practitioner-to-client. Match their voice and structure, not their " +
            "specific facts.");
        builder.AppendLine(
            "- Write the summaryOfUpdate as a neutral, factual account of WHAT CHANGED, grounded only in the " +
            "full text of the document provided below. Do NOT use or style it after the prior-view examples - " +
            "the examples steer the companyView only.");
        builder.AppendLine(
            "- Ground everything in the document provided below. Do not copy an example's content or invent facts, " +
            "figures, dates, or obligations the document does not support.");
        builder.AppendLine(
            "- Prose fields are continuous text (no headings, bullet points, markdown, or preamble). Leave a field " +
            "empty rather than guessing.");
        builder.AppendLine(
            "- If no prior examples are provided, still write in a professional advisory style.");

        return builder.ToString();
    }

    /// <summary>
    /// Builds the user prompt: the group-level impact area and tags, the shared block of the group's
    /// carried updates (each with its full text), and the prior Company View records for the jurisdiction
    /// (house-style exemplars).
    /// </summary>
    public static string BuildUserPrompt(
        TopicGroupContext context,
        string updatesBlock,
        string? impactArea,
        IReadOnlyList<string> tags,
        IReadOnlyList<CompanyViewRecord> priorViews)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"Topic group: {context.TopicGroup.Name}");
        builder.AppendLine($"Jurisdiction: {context.Run.Jurisdiction}");

        if (!string.IsNullOrWhiteSpace(impactArea))
        {
            builder.AppendLine($"Impact area (assigned for this group): {impactArea}");
        }

        if (tags.Count > 0)
        {
            builder.AppendLine($"Tags (assigned for this group): {string.Join(", ", tags)}");
        }

        builder.AppendLine();
        builder.AppendLine(updatesBlock);

        if (priorViews.Count == 0)
        {
            builder.AppendLine(
                "=== Prior Company View records for this jurisdiction ===\n(none available - write in a professional advisory style)");
        }
        else
        {
            builder.AppendLine($"=== Prior Company View records for {context.Run.Jurisdiction} (house-style examples; do NOT copy their facts) ===");
            var index = 1;
            foreach (var prior in priorViews)
            {
                builder.AppendLine($"Example {index++}:");
                AppendExemplarField(builder, "Title", prior.TitleOfUpdate);
                AppendExemplarField(builder, "Impact area", prior.ImpactArea);
                if (prior.Tags.Count > 0)
                {
                    builder.AppendLine($"  Tags: {string.Join(", ", prior.Tags)}");
                }

                AppendExemplarField(builder, "Level of authority", prior.LevelOfAuthority);
                AppendExemplarField(builder, "Status of change", prior.StatusOfChange);
                AppendExemplarField(builder, "Regulator", prior.Regulator);
                AppendExemplarField(builder, "Company View", prior.CompanyView);
                builder.AppendLine();
            }
        }

        builder.Append("Produce the Company View record for this regulatory document now.");
        return builder.ToString();
    }

    private static void AppendExemplarField(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"  {label}: {Clip(value, MaxCharsPerExemplarField)}");
        }
    }

    private static string Clip(string text, int maxChars) =>
        text.Length > maxChars ? text[..maxChars] : text;
}
