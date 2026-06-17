namespace AgenticRagScannerApi.Core.Contracts;

/// <summary>
/// Structured output of the relevance-eval / review agent for ONE pass (transient).
/// The loop controller applies the accuracy override, and the service maps this into the pass's
/// recorded Review. Mirrors the reference repo's ReviewDecision.
/// </summary>
public sealed class ReviewDecision
{
    /// <summary>Why the agent chose to retry or finalize (the loop-level reasoning).</summary>
    public required string ThoughtProcess { get; init; }

    /// <summary>The agent's raw decision, before any override.</summary>
    public LoopDecision Decision { get; init; }

    /// <summary>Per-item verdicts over the current pass's results.</summary>
    public IReadOnlyList<ItemVerdict> Items { get; init; } = [];
}
