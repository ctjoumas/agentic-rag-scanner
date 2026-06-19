using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// LLM agent (stubbed in Epic 2): turns the topic group's keyword/synonym OR-list into a single
/// focused search query for the current pass. On re-loops it consults <see cref="TopicGroupContext.History"/>
/// to rotate synonym coverage and avoid a redundant query - breadth comes from the agentic loop
/// (one query per pass), not from emitting many queries at once. The real implementation
/// (Foundry-backed) lands in Epic 3 - this interface freezes its I/O shape now.
/// </summary>
public interface IQuerySynthesisAgent
{
    /// <summary>Synthesizes a single non-redundant query for the current pass.</summary>
    Task<string> SynthesizeAsync(TopicGroupContext context, CancellationToken cancellationToken = default);
}
