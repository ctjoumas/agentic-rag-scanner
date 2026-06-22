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
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, RunContext run, CancellationToken cancellationToken = default);
}
