using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Epic 8 (story 8.2) LLM agent that assigns exactly one impact area to a topic group as a whole. It is
/// single-label over a closed set - the approved impact-area vocabulary loaded from Cosmos at runtime
/// (RegDocs, <c>doc_type = "ImpactAreas"</c>) - and grounds on the topic group plus the vetted full-text
/// documents of ALL the group's carried updates, snapshotted at the end of the agentic RAG loop. It runs
/// ONCE per group (not per item). Kept separate from the multi-label Tags agent (story 8.3) so each is
/// one LLM task per prompt and can be tuned/evaluated independently.
/// </summary>
public interface IImpactAreaAgent
{
    /// <summary>
    /// Selects the single best impact area (from the approved vocabulary) for the group's carried
    /// <paramref name="items"/>, grounded on their vetted full-text snapshots in
    /// <paramref name="fullTextByItemId"/> (keyed by <see cref="ResultItem.Id"/>; a missing/null entry
    /// means the full text was unavailable). Returns the canonical impact area, or <see langword="null"/>
    /// when the group carried nothing, the vocabulary is empty, or the model returned an off-list/failed
    /// result (a wrong single-label guess is worse than none in a compliance context).
    /// </summary>
    Task<string?> SelectAsync(
        IReadOnlyList<ResultItem> items,
        IReadOnlyDictionary<string, string?> fullTextByItemId,
        TopicGroupContext context,
        CancellationToken cancellationToken = default);
}
