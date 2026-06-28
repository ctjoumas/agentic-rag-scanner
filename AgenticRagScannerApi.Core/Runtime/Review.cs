using AgenticRagScannerApi.Core.Contracts;

namespace AgenticRagScannerApi.Core.Runtime;

/// <summary>
/// The recorded review/eval outcome for ONE loop pass (lives on <see cref="LoopPass"/>).
/// Built from the eval agent's <see cref="ReviewDecision"/> after the loop controller applies the
/// recall override. Self-contained so a pass can be inspected without cross-referencing arrays.
/// </summary>
public sealed class Review
{
    /// <summary>Why this pass chose to retry or finalize.</summary>
    public required string ThoughtProcess { get; init; }

    /// <summary>The eval agent's raw decision.</summary>
    public LoopDecision LlmDecision { get; init; }

    /// <summary>The decision actually applied, after the recall override.</summary>
    public LoopDecision FinalDecision { get; init; }

    /// <summary>True when the override changed the decision.</summary>
    public bool DecisionOverride { get; init; }

    /// <summary>Reason for the override, if any.</summary>
    public string? OverrideReason { get; init; }

    /// <summary>Items vetted this pass (Relevant/Borderline).</summary>
    public List<ResultItem> Vetted { get; } = [];

    /// <summary>Items discarded this pass (NotRelevant), kept for audit.</summary>
    public List<ResultItem> Discarded { get; } = [];
}
