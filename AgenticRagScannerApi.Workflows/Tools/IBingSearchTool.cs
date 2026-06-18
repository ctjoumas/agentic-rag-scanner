using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Tools;

/// <summary>
/// The Bing search tool/connector the loop invokes after query synthesis. It is a deterministic
/// tool - NOT an LLM agent. The allowlist hook scopes results to the run's primary-source allowlist.
/// </summary>
/// <remarks>
/// Epic 2 returns canned hits. Epic 4 wires the real <em>Grounding with Bing Search</em> restricted
/// to the allowlist at query time. <em>Grounding with Bing Custom Search</em> is invoked only inside
/// a Foundry Agent and is intentionally not registered here as a deterministic loop tool.
/// </remarks>
public interface IBingSearchTool
{
    /// <summary>Runs the (canned) search for <paramref name="query"/>, scoped to the run allowlist.</summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, RunContext run, CancellationToken cancellationToken = default);
}
