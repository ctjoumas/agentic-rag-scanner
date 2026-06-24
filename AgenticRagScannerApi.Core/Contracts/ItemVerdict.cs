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

    // The eval agent reads dates from the full text and distinguishes publication vs effective vs
    // tax-year applicability. Dates are a signal, not a hard filter: low/unknown confidence leans an
    // item toward BORDERLINE rather than dropping it. The loop controller maps these onto the ResultItem.

    /// <summary>When the guidance/legislation was posted.</summary>
    public DateOnly? PublicationDate { get; init; }

    /// <summary>When the rule actually takes effect (may differ from publication).</summary>
    public DateOnly? EffectiveDate { get; init; }

    /// <summary>Start of any "applies from / applies to" period (e.g. tax-year applicability).</summary>
    public DateOnly? AppliesFrom { get; init; }

    /// <summary>End of any "applies from / applies to" period.</summary>
    public DateOnly? AppliesTo { get; init; }

    /// <summary>Confidence in the extracted dates.</summary>
    public DateConfidence DateConfidence { get; init; }
}
