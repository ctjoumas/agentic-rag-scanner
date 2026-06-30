using AgenticRagScannerApi.Core.Contracts;

namespace AgenticRagScannerApi.Workflows.Pipeline;

/// <summary>
/// A search hit paired with its fetched/cleaned full text. Produced by the Fetch &amp; Clean step and
/// consumed by the Relevance Eval agent. When full-text fetch fails, the step sets
/// <see cref="Unverified"/> with no cleaned text (the item is flagged, never dropped). This is workflow
/// plumbing - intermediate loop state - and intentionally lives in Workflows, not the Core contracts.
/// </summary>
public sealed class FetchedDocument
{
    /// <summary>The originating search hit (URL, provenance).</summary>
    public required SearchHit Hit { get; init; }

    /// <summary>Cleaned full text (HTML/PDF stripped of boilerplate); null when the fetch failed.</summary>
    public string? CleanedText { get; init; }

    /// <summary>True when full-text fetch failed and the document was flagged as unverified.</summary>
    public bool Unverified { get; init; }
}
