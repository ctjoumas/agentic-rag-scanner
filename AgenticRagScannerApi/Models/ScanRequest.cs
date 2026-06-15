using System.ComponentModel.DataAnnotations;

namespace AgenticRagScannerApi.Models;

/// <summary>
/// Manual horizon-scan trigger payload: a date + jurisdiction + the selected
/// topic groups to scan (architecture-context.md §2). Because topic groups are
/// keyword OR-lists, each one becomes its own parallel workflow when the
/// orchestration is implemented (§3).
/// </summary>
public class ScanRequest
{
    /// <summary>
    /// The scan reference date. Used later as the requested-window anchor by the
    /// effective-date-aware relevance evaluation.
    /// </summary>
    [Required]
    public DateOnly? AsOfDate { get; set; }

    /// <summary>Jurisdiction to scan, e.g. "United Kingdom".</summary>
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string Jurisdiction { get; set; } = string.Empty;

    /// <summary>
    /// Selected topic groups (dense OR-lists of keyword/synonym phrases). Each
    /// fans out to one MAF workflow under a shared throttle.
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one topic group must be selected.")]
    public IReadOnlyList<string> TopicGroups { get; set; } = [];
}
