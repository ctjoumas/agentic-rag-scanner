namespace AgenticRagScannerApi.Models;

/// <summary>
/// Manual horizon-scan trigger payload: a date + jurisdiction + the selected
/// topic groups to scan (architecture-context.md §2). Because each topic group is
/// a comma-separated list of related keywords, each one becomes its own parallel
/// workflow when the orchestration is implemented (§3).
/// </summary>
public class ScanRequest
{
    /// <summary>
    /// The scan reference date. Used later as the requested-window anchor by the
    /// effective-date-aware relevance evaluation.
    /// </summary>
    public DateOnly? AsOfDate { get; set; }

    /// <summary>Jurisdiction to scan, e.g. "United Kingdom".</summary>
    public string Jurisdiction { get; set; } = string.Empty;

    /// <summary>
    /// Selected topic groups. Each entry is one topic group expressed as a
    /// comma-separated list of related keyword/synonym phrases (for example
    /// "Employee NIC, Income Tax, ITEPA 2003, Salary Sacrifice"). The whole group
    /// is processed as a single unit - one synthesized query per loop pass - and
    /// each group fans out to its own MAF workflow under a shared throttle.
    /// </summary>
    public IReadOnlyList<string> TopicGroups { get; set; } = [];
}
