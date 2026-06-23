namespace AgenticRagScannerApi.Core.Contracts;

/// <summary>
/// One regulatory item as it moves through the pipeline and is ultimately persisted
/// (one versioned doc per item per run - horizon-scanner-architecture.md, step 16).
/// Fields are populated progressively: relevance + dates in the loop, then enrichment,
/// categorization, summary, and the quality-gate authority stamp.
/// </summary>
public sealed class ResultItem
{
    // --- identity / audit ---

    /// <summary>The run this item belongs to.</summary>
    public required string RunId { get; init; }

    /// <summary>The topic group that surfaced this item.</summary>
    public required string GroupId { get; init; }

    /// <summary>Stable identifier (hash of the primary source URL[s]) for idempotent upsert.</summary>
    public required string Id { get; init; }

    /// <summary>Document version (one versioned doc per item per run).</summary>
    public int Version { get; init; } = 1;

    /// <summary>The loop pass on which this item was first vetted (observability).</summary>
    public int FoundOnPass { get; set; }

    // --- provenance ---

    /// <summary>Primary source URL(s). Multiple primary sources are allowed.</summary>
    public required IReadOnlyList<string> SourceUrls { get; init; }

    /// <summary>Primary source host/domain.</summary>
    public string? Domain { get; init; }

    /// <summary>
    /// Blob reference (path/URI) to the cleaned full text fetched at eval time.
    /// Persisted to blob for audit/provenance: the live URL can change or 404, so we snapshot exactly
    /// what the Relevance Eval agent read.
    /// </summary>
    public string? FullTextBlobUri { get; set; }

    // --- relevance (set in the loop) ---

    /// <summary>Three-way relevance verdict.</summary>
    public Verdict Verdict { get; set; }

    /// <summary>Eval rationale for the verdict.</summary>
    public string? EvalRationale { get; set; }

    /// <summary>True when full-text fetch failed and we fell back to the Bing summary.</summary>
    public bool Unverified { get; set; }

    // --- effective-date-aware eval ---

    /// <summary>When the guidance/legislation was posted.</summary>
    public DateOnly? PublicationDate { get; set; }

    /// <summary>When the rule actually takes effect (may differ from publication).</summary>
    public DateOnly? EffectiveDate { get; set; }

    /// <summary>Start of any "applies from / applies to" period.</summary>
    public DateOnly? AppliesFrom { get; set; }

    /// <summary>End of any "applies from / applies to" period.</summary>
    public DateOnly? AppliesTo { get; set; }

    /// <summary>Confidence in the extracted dates.</summary>
    public DateConfidence DateConfidence { get; set; }

    // --- enrich / categorize / summarize ---

    /// <summary>Plain-English "what it does" summary (enrichment).</summary>
    public string? WhatItDoes { get; set; }

    /// <summary>Impact area (categorize).</summary>
    public string? ImpactArea { get; set; }

    /// <summary>Regulator (categorize).</summary>
    public string? Regulator { get; set; }

    /// <summary>Approved tags only (controlled vocabulary).</summary>
    public IReadOnlyList<string> Tags { get; set; } = [];

    /// <summary>Plain-English impact summary (summarize and impact).</summary>
    public string? ImpactSummary { get; set; }

    // --- quality gate ---

    /// <summary>Source authority tier stamped by the quality gate.</summary>
    public LevelOfAuthority LevelOfAuthority { get; set; }
}
