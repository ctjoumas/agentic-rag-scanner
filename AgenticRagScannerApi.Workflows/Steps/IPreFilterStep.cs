using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Steps;

/// <summary>
/// Deterministic pre-filter step (scaffold for Epic 2): normalizes hit URLs, drops invalid ones, and
/// de-duplicates against <see cref="SearchHistory.ProcessedKeys"/> (which also covers earlier passes
/// in the same run). The full pure/unit-tested pre-filter - including cross-group dedupe and URL
/// reachability - lands in Epic 4 (story 4.3).
/// </summary>
public interface IPreFilterStep
{
    /// <summary>Returns the subset of <paramref name="hits"/> that are valid and not already seen this run.</summary>
    IReadOnlyList<SearchHit> Filter(IReadOnlyList<SearchHit> hits, SearchHistory history);
}
