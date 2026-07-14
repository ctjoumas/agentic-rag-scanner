using System.Globalization;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Common;

/// <summary>
/// Helpers for the scan's requested date range, derived from <see cref="RunContext"/>. Centralizes the
/// end-date fallback (a null <see cref="RunContext.EndDate"/> resolves to the run's start date) so the
/// query-synthesis prompt, the relevance-eval prompt/agent, and the loop controller's out-of-window
/// filter all agree on one date-range definition instead of repeating the logic.
/// </summary>
public static class ScanDateRange
{
    /// <summary>
    /// Resolves the effective bounds: the inclusive lower bound (null = no lower cutoff) and the inclusive
    /// upper bound (a null <see cref="RunContext.EndDate"/> falls back to the run's start date).
    /// </summary>
    public static (DateOnly? Start, DateOnly End) Resolve(RunContext run)
    {
        var end = run.EndDate ?? DateOnly.FromDateTime(run.StartedAtUtc.UtcDateTime);
        return (run.StartDate, end);
    }

    /// <summary>
    /// Formats the range for a prompt: <c>"yyyy-MM-dd to yyyy-MM-dd"</c> when a start date is set, or
    /// <c>"on or before yyyy-MM-dd"</c> when the lower bound is open.
    /// </summary>
    public static string Format(RunContext run)
    {
        var (start, end) = Resolve(run);
        var endText = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return start is { } value
            ? $"{value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} to {endText}"
            : $"on or before {endText}";
    }
}
