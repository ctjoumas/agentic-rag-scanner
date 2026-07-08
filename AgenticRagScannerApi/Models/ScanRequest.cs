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
    /// Inclusive lower bound of the scan window. Used together with <see cref="EndDate"/> to scope
    /// search and the effective-date-aware relevance evaluation to a date range. When null, no lower
    /// cutoff is applied.
    /// </summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>
    /// Inclusive upper bound of the scan window. Used together with <see cref="StartDate"/> to scope
    /// search and the effective-date-aware relevance evaluation to a date range. When null, the run's
    /// start date is used as the upper cutoff.
    /// </summary>
    public DateOnly? EndDate { get; set; }

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
