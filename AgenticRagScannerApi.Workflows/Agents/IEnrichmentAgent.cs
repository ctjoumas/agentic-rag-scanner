using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// LLM agent (stubbed in Epic 2): post-verdict enrichment only (relevance is already decided). Adds
/// a plain-English "what it does" summary and enriched metadata to a carried item. The real
/// implementation lands in Epic 7 - this interface freezes its I/O shape now.
/// </summary>
public interface IEnrichmentAgent
{
    /// <summary>Enriches a carried (Relevant/Borderline) item in place and returns it.</summary>
    Task<ResultItem> EnrichAsync(ResultItem item, TopicGroupContext context, CancellationToken cancellationToken = default);
}
