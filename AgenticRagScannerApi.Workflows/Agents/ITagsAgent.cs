using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Epic 8 (story 8.3) LLM agent that selects one or more tags for a topic group as a whole. It is
/// multi-label over the approved tag vocabulary loaded from Cosmos at runtime (RegDocs,
/// <c>doc_type = "tags"</c>) and grounds on the topic group plus the vetted full-text documents of ALL
/// the group's carried updates, snapshotted at the end of the agentic RAG loop. It runs ONCE per group
/// (not per item) and is a separate agent / LLM call from the single-label Impact Area agent (story 8.2)
/// - one LLM task per prompt, independently tunable and evaluable.
/// </summary>
public interface ITagsAgent
{
    /// <summary>
    /// Selects the applicable tags (from the approved vocabulary) for the group's carried
    /// <paramref name="items"/>, grounded on their vetted full-text snapshots in
    /// <paramref name="fullTextByItemId"/> (keyed by <see cref="ResultItem.Id"/>; a missing/null entry
    /// means the full text was unavailable). Returns the canonical tags (possibly empty); an empty list is
    /// also returned when the group carried nothing, the vocabulary is empty, or the model call failed.
    /// </summary>
    Task<IReadOnlyList<string>> SelectAsync(
        IReadOnlyList<ResultItem> items,
        IReadOnlyDictionary<string, string?> fullTextByItemId,
        TopicGroupContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects the applicable tags (from the approved vocabulary) for ONE vetted document (Option A:
    /// per-item categorisation), grounded on that item's <paramref name="fullText"/> snapshot
    /// (<see langword="null"/> when unavailable). Returns the canonical tags (possibly empty); empty also
    /// when the vocabulary is empty or the model call failed.
    /// </summary>
    Task<IReadOnlyList<string>> SelectAsync(
        ResultItem item,
        string? fullText,
        TopicGroupContext context,
        CancellationToken cancellationToken = default);
}
