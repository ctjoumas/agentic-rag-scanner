using System.ComponentModel.DataAnnotations;

namespace AgenticRagScannerApi.Workflows.Configuration;

/// <summary>
/// Tuning knobs for the Query Synthesis MAF agent (Epic 3, story 3.3). All values are optional with
/// sensible defaults, so the agent works without a "QuerySynthesis" configuration section.
/// </summary>
public sealed class QuerySynthesisOptions
{
    public const string SectionName = "QuerySynthesis";

    /// <summary>Total attempts (initial call + retries) to obtain valid JSON before falling back.</summary>
    [Range(1, 5)]
    public int MaxAttempts { get; set; } = 2;

    /// <summary>Sampling temperature; moderate so successive passes rotate synonym coverage.</summary>
    [Range(0.0, 2.0)]
    public float Temperature { get; set; } = 0.4f;
}
