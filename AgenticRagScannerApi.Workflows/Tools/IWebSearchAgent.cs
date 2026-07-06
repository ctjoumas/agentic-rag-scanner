using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Tools;

/// <summary>
/// The web search agent the loop invokes after query synthesis. The allowlist hook scopes results to
/// the run's primary-source allowlist.
/// </summary>
/// <remarks>
/// Epic 2 returns canned hits. Epic 4 runs a pre-provisioned Foundry Web Search agent (configured in the
/// portal with Grounding with Bing Custom Search) and maps its URL citations into hits scoped to the
/// allowlist at query time.
/// </remarks>
public interface IWebSearchAgent
{
    /// <summary>Runs the web search for <paramref name="query"/>, scoped to the run allowlist.</summary>
    Task<WebSearchResult> SearchAsync(string query, RunContext run, CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of a web search. Distinguishes a genuine empty result (search ran, returned no citations)
/// from a failure (timeout/transport error after retries): the loop degrades gracefully in both cases,
/// but only a failure is surfaced on the pass so a zero-result group caused by a broken search is not
/// silently reported as a clean, completed empty scan.
/// </summary>
/// <param name="Hits">The mapped, allowlist-scoped hits (empty on failure or a genuine empty result).</param>
/// <param name="Failed"><see langword="true"/> when the search failed (timeout/error) rather than genuinely returning no hits.</param>
/// <param name="FailureReason">A short, human-readable reason when <paramref name="Failed"/> is <see langword="true"/>.</param>
public sealed record WebSearchResult(IReadOnlyList<SearchHit> Hits, bool Failed, string? FailureReason = null)
{
    /// <summary>A successful search (which may legitimately have returned zero hits).</summary>
    public static WebSearchResult Ok(IReadOnlyList<SearchHit> hits) => new(hits, Failed: false);

    /// <summary>A failed search (timeout/transport error): no hits, carrying the failure reason.</summary>
    public static WebSearchResult Failure(string reason) => new([], Failed: true, reason);
}
