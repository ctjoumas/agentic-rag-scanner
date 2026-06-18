namespace AgenticRagScannerApi.Core.Contracts;

/// <summary>
/// Outcome of a single agentic-loop pass: keep searching or stop.
/// Drives the loop controller / MAF conditional (horizon-scanner-architecture.md, step 10).
/// </summary>
public enum LoopDecision
{
    /// <summary>Goal not yet met - synthesize a new, non-redundant query and run another pass.</summary>
    Retry,

    /// <summary>Enough data gathered - exit the loop and route verdicts downstream.</summary>
    Finalize
}
