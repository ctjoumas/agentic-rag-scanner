using System.ComponentModel.DataAnnotations;

namespace AgenticRagScannerApi.Workflows.Configuration;

/// <summary>
/// Tuning knobs for the Company View MAF agent (Epic 8, story 8.5). All values are optional with
/// sensible defaults, so the agent works without a "CompanyView" configuration section.
/// </summary>
public sealed class CompanyViewOptions
{
    public const string SectionName = "CompanyView";

    /// <summary>Cap on prior-view exemplars fed to the model as house-style guidance, to bound tokens.</summary>
    [Range(1, 20)]
    public int MaxExemplars { get; set; } = 5;

    /// <summary>Low-ish sampling temperature so the advice stays grounded and on-style without being wooden.</summary>
    [Range(0.0, 2.0)]
    public float Temperature { get; set; } = 0.3f;
}
