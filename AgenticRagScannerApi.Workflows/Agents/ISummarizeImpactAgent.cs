using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// LLM agent (stubbed in Epic 2): RAG over the in-memory <see cref="SearchHistory"/> to produce a
/// plain-English impact summary with effective-date framing. The real implementation lands in
/// Epic 7 - this interface freezes its I/O shape now.
/// </summary>
public interface ISummarizeImpactAgent
{
    /// <summary>Produces the plain-English impact summary for an enriched item in place and returns it.</summary>
    Task<ResultItem> SummarizeAsync(ResultItem item, TopicGroupContext context, CancellationToken cancellationToken = default);
}
