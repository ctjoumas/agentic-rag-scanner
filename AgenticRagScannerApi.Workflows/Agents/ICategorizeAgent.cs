using AgenticRagScannerApi.Core.Contracts;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// LLM agent (stubbed in Epic 2): assigns impact area, regulator, and approved tags from the
/// controlled vocabulary only. The real implementation lands in Epic 7 - this interface freezes its
/// I/O shape now.
/// </summary>
public interface ICategorizeAgent
{
    /// <summary>Assigns category fields (impact area, regulator, approved tags) to an enriched item in place and returns it.</summary>
    Task<ResultItem> CategorizeAsync(ResultItem item, CancellationToken cancellationToken = default);
}
