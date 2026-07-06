using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Epic 8 (story 8.5) LLM agent that produces ONE consolidated <see cref="DeloitteViewRecord"/> for a
/// topic group in a single call: a neutral, factual <c>SummaryOfUpdate</c> of what changed AND the
/// practitioner-style <c>DeloitteView</c> advice. Both are grounded on the <em>full text</em> of the
/// group's carried regulatory updates (plus the group-level impact area and tags); the DeloitteView
/// additionally steers its house style/tone via RAG over prior Deloitte View records retrieved by
/// jurisdiction (the summary does NOT use those historical records). The record is shaped to match the
/// historical <c>RegulatoryUpdatesCsv</c>. Runs once per group.
/// </summary>
public interface IDeloitteViewAgent
{
    /// <summary>
    /// Produces the group's consolidated Deloitte View record from its carried <paramref name="items"/>,
    /// grounded on each item's full text supplied in <paramref name="fullTextByItemId"/> (keyed by
    /// <see cref="ResultItem.Id"/>; a missing/null entry means the full text was unavailable). The
    /// group-level <paramref name="impactArea"/> and <paramref name="tags"/> (computed once over all the
    /// items' full text) are stamped onto the record and provided to the model as grounding. Returns
    /// <see langword="null"/> when the group carried no items (nothing to aggregate).
    /// </summary>
    Task<DeloitteViewRecord?> GenerateAsync(
        IReadOnlyList<ResultItem> items,
        IReadOnlyDictionary<string, string?> fullTextByItemId,
        string? impactArea,
        IReadOnlyList<string> tags,
        TopicGroupContext context,
        CancellationToken cancellationToken = default);
}
