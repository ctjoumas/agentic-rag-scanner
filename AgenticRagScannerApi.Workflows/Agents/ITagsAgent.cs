using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Epic 8 (story 8.3) LLM agent that selects one or more tags for a single vetted document. It is
/// multi-label over the approved tag vocabulary loaded from Cosmos at runtime (RegDocs,
/// <c>doc_type = "tags"</c>) and grounds on that document's vetted full-text snapshot. Runs once per
/// vetted document and is a separate agent / LLM call from the single-label Impact Area agent (story 8.2)
/// - one LLM task per prompt, independently tunable and evaluable.
/// </summary>
public interface ITagsAgent
{
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
