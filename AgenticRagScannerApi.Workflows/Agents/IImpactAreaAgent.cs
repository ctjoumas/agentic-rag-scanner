using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Epic 8 (story 8.2) LLM agent that assigns exactly one impact area to a single vetted document. It is
/// single-label over a closed set - the approved impact-area vocabulary loaded from Cosmos at runtime
/// (RegDocs, <c>doc_type = "ImpactAreas"</c>) - and grounds on that document's vetted full-text snapshot.
/// Runs once per vetted document. Kept separate from the multi-label Tags agent (story 8.3) so each is one
/// LLM task per prompt and can be tuned/evaluated independently.
/// </summary>
public interface IImpactAreaAgent
{
    /// <summary>
    /// Selects the single best impact area (from the approved vocabulary) for ONE vetted document
    /// (Option A: per-item categorisation), grounded on that item's <paramref name="fullText"/> snapshot
    /// (<see langword="null"/> when unavailable). Returns the canonical impact area, or
    /// <see langword="null"/> when the vocabulary is empty or the model returned an off-list/failed result.
    /// </summary>
    Task<string?> SelectAsync(
        ResultItem item,
        string? fullText,
        TopicGroupContext context,
        CancellationToken cancellationToken = default);
}
