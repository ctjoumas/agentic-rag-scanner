namespace AgenticRagScannerApi.Core.Contracts;

/// <summary>
/// The eval agent's per-item judgement for one current-pass result. Part of <see cref="ReviewDecision"/>.
/// "Valid" (carried forward to vetted) = <see cref="Verdict.Relevant"/> or <see cref="Verdict.Borderline"/>;
/// "invalid" (discarded) = <see cref="Verdict.NotRelevant"/>.
/// </summary>
public sealed class ItemVerdict
{
    /// <summary>Index into the current pass's results that this judgement applies to.</summary>
    public int Index { get; init; }

    /// <summary>The three-way relevance verdict for this item.</summary>
    public Verdict Verdict { get; init; }

    /// <summary>Short rationale for the verdict (carried into the audit trail).</summary>
    public string? Rationale { get; init; }
}
