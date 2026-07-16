using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Epic 8 (story 8.5) LLM agent that produces ONE <see cref="CompanyViewRecord"/> for a single vetted
/// document in a single call: a neutral, factual <c>SummaryOfUpdate</c> of what changed AND the
/// practitioner-style <c>CompanyView</c> advice. Both are grounded on the <em>full text</em> of that
/// document (plus its impact area and tags); the CompanyView additionally steers its house style/tone
/// from prior Company View exemplars passed in by the finalize step (fetched once per group, by
/// jurisdiction) - the summary does NOT use those historical records. The record is shaped to match the
/// historical <c>RegulatoryUpdatesCsv</c>.
/// </summary>
public interface ICompanyViewAgent
{
    /// <summary>
    /// Produces the Company View record for ONE vetted document (Option A: one per document), grounded on
    /// that item's <paramref name="fullText"/> snapshot plus its <paramref name="impactArea"/> and
    /// <paramref name="tags"/>. House-style <paramref name="priorViews"/> exemplars are passed in (fetched
    /// once per group by the finalize step, already capped) rather than retrieved by the agent.
    /// </summary>
    Task<CompanyViewRecord?> GenerateAsync(
        ResultItem item,
        string? fullText,
        string? impactArea,
        IReadOnlyList<string> tags,
        IReadOnlyList<CompanyViewRecord> priorViews,
        TopicGroupContext context,
        CancellationToken cancellationToken = default);
}
