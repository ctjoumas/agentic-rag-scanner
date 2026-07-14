using System.ComponentModel.DataAnnotations;

namespace AgenticRagScannerApi.Configuration;

/// <summary>
/// Binds to the "RegulatoryUpdatesCsv" configuration section. Points at the local CSV of historical
/// regulatory updates + Company Views used to ground the Company View agent (Epic 8, story 8.5) for
/// local testing. In production the source is relational (SQL) behind
/// <see cref="Workflows.CompanyView.IPriorCompanyViewSource"/>, so the path is optional and an empty
/// or missing file degrades gracefully (no prior views, rather than a startup failure).
/// </summary>
public sealed class RegulatoryUpdatesCsvOptions
{
    public const string SectionName = "RegulatoryUpdatesCsv";

    /// <summary>Absolute path to the historical Company Views CSV. Empty when the CSV source is not used.</summary>
    public string FilePath { get; set; } = string.Empty;
}
