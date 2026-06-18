using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;

namespace AgenticRagScannerApi.Workflows.Steps;

/// <summary>
/// Deterministic verdict routing (stub for Epic 2), applied once the loop finalizes. RELEVANT and
/// BORDERLINE items are carried forward to enrichment (BORDERLINE remains flagged via its
/// <see cref="ResultItem.Verdict"/>); NOT_RELEVANT items are dropped and logged for audit.
/// </summary>
public interface IVerdictRouting
{
    /// <summary>Returns the vetted items to carry into the enrichment chain; logs dropped items.</summary>
    IReadOnlyList<ResultItem> Route(TopicGroupContext context);
}
