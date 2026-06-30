using System.Globalization;
using System.Text;
using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Pipeline;

namespace AgenticRagScannerApi.Workflows.Prompts;

/// <summary>
/// System- and user-prompt builders for the Relevance Eval agent (Epic 6, story 6.1). The agent makes a
/// single full-text LLM call that classifies each current-pass document RELEVANT / BORDERLINE /
/// NOT_RELEVANT, extracts effective-date-aware fields (publication vs effective vs tax-year), and judges
/// whether the group's goal is met (which informs the loop decision). The <c>ThoughtProcess</c> it returns
/// is written in a defined steer shape (story 6.5, Option A) that the v4 query-synthesis prompt interprets
/// on the next pass. Prompts are versioned via <see cref="Version"/> and version-locked with
/// <see cref="QuerySynthesisPrompt.Version"/> so eval runs attribute output changes to prompt changes.
/// </summary>
public static class RelevanceEvalPrompt
{
    /// <summary>Prompt version - bump when the instructions change (version-locked with the query-synth prompt).</summary>
    public const string Version = "v2";

    /// <summary>
    /// Builds the system prompt: role, the three-way verdict rubric, effective-date rules, and the
    /// ThoughtProcess steer shape. The response shape is enforced by Structured Outputs (a JSON schema),
    /// so the prompt describes the judgement, not the JSON wrapper.
    /// </summary>
    public static string BuildSystemPrompt(string groupName, string jurisdiction, string asOfDate) =>
        $$"""
        You are a relevance-and-compliance reviewer for a regulatory horizon-scanning system. You read
        the FULL TEXT of candidate documents retrieved for one topic group and decide, for each one,
        whether it is a genuine primary-source regulatory update for the '{{groupName}}' theme in the
        {{jurisdiction}} jurisdiction. The scan's reference ("as-of") date is {{asOfDate}}.

        A topic group is ONE coherent theme expressed through many surface forms (synonyms, aliases,
        acronyms that co-occur on authoritative pages). Judge each document against that whole theme.

        Per-document verdict (assign exactly one):
        - RELEVANT: a primary-source (government, regulator, legislation, official guidance) item that
          materially updates, changes, or states the rules for this theme and applies on/around the
          as-of date.
        - BORDERLINE: on-theme and plausibly useful but weaker - secondary/commentary source, ambiguous
          applicability, partial coverage, or dates you cannot pin down confidently. Carry it forward
          flagged; do NOT drop it.
        - NOT_RELEVANT: off-theme, a different jurisdiction, marketing/navigation/boilerplate, or clearly
          superseded/out-of-scope content.

        Effective-date awareness (dates are a SIGNAL, not a hard filter):
        - Distinguish PUBLICATION date (when posted), EFFECTIVE / in-force date (when the rule takes
          effect - may differ from publication), and tax-year / "applies from - applies to" APPLICABILITY.
        - Fill publicationDate, effectiveDate, appliesFrom, appliesTo with ISO yyyy-MM-dd where the text
          supports them; otherwise leave them null. Never invent a date.
        - Set dateConfidence to HIGH (explicit, unambiguous dates), MEDIUM (inferable), LOW (vague /
          conflicting), or UNKNOWN (no usable date). Low/Unknown confidence should lean an otherwise
          on-theme item toward BORDERLINE rather than NOT_RELEVANT.
        - Do NOT drop an item solely because its date is old or missing - flag it instead.

        Loop decision (the "decision" field) - judge CUMULATIVE coverage, i.e. everything previously
        vetted (shown to you below) PLUS what this pass adds, not this pass in isolation:
        - FINALIZE if the vetted set now covers the group's theme well enough that another search pass is
          unlikely to add materially new primary-source coverage.
        - RETRY if the theme is still under-covered and another, differently-angled query would likely help.
        You classify ONLY the current pass's documents; the previously-vetted results are context for this
        judgement and must never be re-verdicted.

        ThoughtProcess (write 2-5 sentences in THIS shape so the next query-synthesis pass can act on it):
        - "Missing facets: <facet A>, <facet B> - aspects of the theme that were never retrieved; the next
          query must BROADEN to introduce these." (List concrete facets, or write "Missing facets: none".)
        - "Weak evidence: facets <P>, <Q> are represented but the evidence is thin / secondary /
          low-authority / out-of-date / ambiguous because <reasons>; the next query must DEEPEN or PIVOT
          toward authoritative primary sources on these." (Or write "Weak evidence: none".)
        Be specific and name facets by their real-world terms; this text steers the next search.
        """;

    /// <summary>
    /// Builds the user prompt: the indexed list of fetched documents (url, domain, unverified flag, and
    /// head-truncated full text). The model must return one verdict per index.
    /// </summary>
    public static string BuildUserPrompt(
        TopicGroupContext context,
        IReadOnlyList<FetchedDocument> documents,
        int maxCharsPerDocument)
    {
        var builder = new StringBuilder();
        var pass = context.LoopCount; // current pass already started/appended by the pipeline

        builder.AppendLine($"Topic group: {context.TopicGroup.Name}");
        builder.AppendLine($"Jurisdiction: {context.Run.Jurisdiction}");
        builder.AppendLine($"As-of date: {FormatAsOf(context)}");
        builder.AppendLine($"Pass: {pass} of up to {context.TopicGroup.MaxLoops}");

        builder.AppendLine("Keyword / synonym OR-list defining the theme:");
        foreach (var keyword in context.TopicGroup.Keywords)
        {
            builder.AppendLine($"- {keyword}");
        }

        AppendVettedContext(builder, context);
        AppendHistoryContext(builder, context);

        builder.AppendLine();
        builder.AppendLine(
            $"Documents to evaluate THIS pass ({documents.Count}). These are the ONLY documents to classify - "
            + "return exactly one verdict per index. Do NOT emit verdicts for the previously-vetted results above:");

        for (var i = 0; i < documents.Count; i++)
        {
            var doc = documents[i];
            var hit = doc.Hit;

            builder.AppendLine();
            builder.AppendLine($"=== Document index {i} ===");
            builder.AppendLine($"URL: {hit.Url}");
            if (!string.IsNullOrWhiteSpace(hit.Domain))
            {
                builder.AppendLine($"Domain: {hit.Domain}");
            }

            if (doc.Unverified)
            {
                builder.AppendLine("NOTE: full-text fetch failed; no content is available (treat as unverified).");
            }

            builder.AppendLine("Full text:");
            builder.AppendLine(Truncate(doc.CleanedText, maxCharsPerDocument));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Appends the results already vetted on earlier passes (context only - NOT re-evaluated). This lets
    /// the agent judge CUMULATIVE coverage when deciding FINALIZE vs RETRY, mirroring the reference review
    /// step that shows prior vetted results to the model without re-reviewing them.
    /// </summary>
    private static void AppendVettedContext(StringBuilder builder, TopicGroupContext context)
    {
        var vetted = context.History.Vetted.ToList();

        builder.AppendLine();
        builder.AppendLine(
            $"Previously vetted results carried from earlier passes ({vetted.Count}) - context only, already "
            + "counted toward coverage; do NOT re-evaluate or assign these a verdict:");

        if (vetted.Count == 0)
        {
            builder.AppendLine("- none yet (this is the first pass, or nothing has been carried forward).");
            return;
        }

        foreach (var item in vetted)
        {
            var url = item.SourceUrls.Count > 0 ? item.SourceUrls[0] : "(no url)";
            builder.AppendLine($"- [{item.Verdict}] {url} (found pass {item.FoundOnPass}; dates {FormatDates(item)})");
            if (!string.IsNullOrWhiteSpace(item.EvalRationale))
            {
                builder.AppendLine($"    why: {item.EvalRationale.Trim()}");
            }
        }
    }

    /// <summary>
    /// Appends prior search attempts (query + reviewer steer + decision) so the agent can see what has
    /// already been tried and reasoned, informing both its verdicts and the FINALIZE/RETRY judgement.
    /// </summary>
    private static void AppendHistoryContext(StringBuilder builder, TopicGroupContext context)
    {
        // The current pass is already appended but not yet reviewed, so Review-bearing passes are prior ones.
        var prior = context.History.Passes.Where(p => p.Review is not null).ToList();

        builder.AppendLine();
        builder.AppendLine($"Previous search attempts ({prior.Count}):");

        if (prior.Count == 0)
        {
            builder.AppendLine("- none - this is the first pass.");
            return;
        }

        foreach (var pass in prior)
        {
            builder.AppendLine($"<attempt {pass.Pass}> query: {pass.Query} | decision: {pass.Review!.FinalDecision}");
            if (!string.IsNullOrWhiteSpace(pass.Review.ThoughtProcess))
            {
                builder.AppendLine($"    notes: {pass.Review.ThoughtProcess.Trim()}");
            }
        }
    }

    private static string FormatDates(ResultItem item)
    {
        static string D(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "?";
        return $"pub={D(item.PublicationDate)} eff={D(item.EffectiveDate)} applies={D(item.AppliesFrom)}->{D(item.AppliesTo)} conf={item.DateConfidence}";
    }

    private static string FormatAsOf(TopicGroupContext context) =>
        context.Run.AsOfDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        ?? context.Run.StartedAtUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Truncate(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(no text available)";
        }

        var trimmed = text.Trim();
        return trimmed.Length <= maxChars
            ? trimmed
            : trimmed[..maxChars] + "\n[...truncated...]";
    }
}
