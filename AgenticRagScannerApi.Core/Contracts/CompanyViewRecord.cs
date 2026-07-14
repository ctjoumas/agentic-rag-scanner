namespace AgenticRagScannerApi.Core.Contracts;

/// <summary>
/// A regulatory-update + Company View record shaped to match the customer's historical
/// <c>RegulatoryUpdatesCsv</c> columns (Epic 8, story 8.5). The same model is used two ways:
/// <list type="bullet">
///   <item>as a <em>prior</em> record read from the CSV (a house-style exemplar for RAG), and</item>
///   <item>as the <em>produced</em> aggregate for a topic group - one record per group that rolls up its
///   carried regulatory updates (their summaries, impact areas, and tags) into a single practitioner
///   Company View, grounded in the topic group and steered by the prior records for the jurisdiction.</item>
/// </list>
/// Free-text/date fields are kept as strings to match the CSV's free-form cells faithfully. The internal
/// administrative CSV columns (<c>Update Month</c>, <c>ID</c>, <c>Linked IDs</c>) are intentionally
/// omitted - they are bookkeeping, not content the agent produces.
/// </summary>
public sealed class CompanyViewRecord
{
    /// <summary>CSV: <c>Jurisdiction</c>.</summary>
    public string? Jurisdiction { get; set; }

    /// <summary>CSV: <c>Impact Area</c> (for the aggregate, the distinct impact areas across the group).</summary>
    public string? ImpactArea { get; set; }

    /// <summary>CSV: <c>Tags</c> (for the aggregate, the distinct tags across the group).</summary>
    public IReadOnlyList<string> Tags { get; set; } = [];

    /// <summary>CSV: <c>Title of Update</c>.</summary>
    public string? TitleOfUpdate { get; set; }

    /// <summary>CSV: <c>Summary of Update</c>.</summary>
    public string? SummaryOfUpdate { get; set; }

    /// <summary>CSV: <c>Company View</c> - the practitioner-style client advice.</summary>
    public string? CompanyView { get; set; }

    /// <summary>CSV: <c>Level of Authority</c>.</summary>
    public string? LevelOfAuthority { get; set; }

    /// <summary>CSV: <c>Status of Change</c>.</summary>
    public string? StatusOfChange { get; set; }

    /// <summary>CSV: <c>Announcement date</c>.</summary>
    public string? AnnouncementDate { get; set; }

    /// <summary>CSV: <c>Effective Date of Change</c>.</summary>
    public string? EffectiveDateOfChange { get; set; }

    /// <summary>CSV: <c>Supporting reference</c> (for the aggregate, the source URLs of the carried updates).</summary>
    public string? SupportingReference { get; set; }

    /// <summary>CSV: <c>Regulator</c>.</summary>
    public string? Regulator { get; set; }
}
