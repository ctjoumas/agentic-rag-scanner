using AgenticRagScannerApi.Core.Contracts;

namespace AgenticRagScannerApi.Workflows.Pipeline;

/// <summary>
/// A search hit paired with its fetched/cleaned full text. Produced by the Fetch &amp; Clean step and
/// consumed by the Relevance Eval agent. When full-text fetch fails, the step falls back to the Bing
/// snippet and sets <see cref="Unverified"/> (the item is flagged, never dropped). This is workflow
/// plumbing - intermediate loop state - and intentionally lives in Workflows, not the Core contracts.
/// </summary>
public sealed class FetchedDocument
{
    /// <summary>The originating search hit (URL, snippet, provenance).</summary>
    public required SearchHit Hit { get; init; }

    /// <summary>Cleaned full text (HTML/PDF stripped of boilerplate); snippet fallback if fetch failed.</summary>
    public string? CleanedText { get; init; }

    /// <summary>True when full-text fetch failed and the Bing snippet was used as a fallback.</summary>
    public bool Unverified { get; init; }
}
