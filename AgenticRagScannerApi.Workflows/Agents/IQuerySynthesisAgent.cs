using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// LLM agent (stubbed in Epic 2): turns the topic group's keyword/synonym OR-list into focused
/// search queries. On re-loops it consults <see cref="TopicGroupContext.History"/> to rotate synonym
/// coverage and avoid redundant queries; the agent decides how many queries to emit. The real
/// implementation (Foundry-backed) lands in Epic 3 - this interface freezes its I/O shape now.
/// </summary>
public interface IQuerySynthesisAgent
{
    /// <summary>Synthesizes one or more non-redundant queries for the current pass.</summary>
    Task<IReadOnlyList<string>> SynthesizeAsync(TopicGroupContext context, CancellationToken cancellationToken = default);
}
