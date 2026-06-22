using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Steps;

/// <summary>
/// Deterministic pre-filter step (story 4.3). Canonicalizes hit URLs (see
/// <see cref="UrlCanonicalizer"/>), drops invalid ones, and de-duplicates on two levels:
/// <list type="bullet">
///   <item><see cref="SearchHistory.ProcessedKeys"/> - per-group, checkpoint-backed (also covers
///         earlier passes in the same group on resume);</item>
///   <item><see cref="RunContext.SeenUrlKeys"/> - run-level, in memory, so the same URL is never
///         fetched and evaluated twice by <em>different</em> topic groups (cross-group dedupe).</item>
/// </list>
/// Pure and side-effect-scoped to the two seen-sets, so it is fully unit-testable without I/O.
/// </summary>
public interface IPreFilterStep
{
    /// <summary>
    /// Returns the subset of <paramref name="hits"/> that are valid http(s) URLs and have not already
    /// been seen by this group (any pass) or by any other group in the same run.
    /// </summary>
    IReadOnlyList<SearchHit> Filter(IReadOnlyList<SearchHit> hits, TopicGroupContext context);
}
