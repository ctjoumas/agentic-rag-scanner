namespace AgenticRagScannerApi.Core.Runtime;

/// <summary>
/// A curated topic group: a dense OR-list of keyword/synonym/alias phrases that drives query
/// synthesis, plus a per-group loop cap. Larger synonym-heavy groups may use a higher cap so
/// synthesis can rotate coverage across more passes (horizon-scanner-architecture.md, step 10).
/// </summary>
public sealed class TopicGroup
{
    /// <summary>Stable identifier for the group.</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable group name (e.g. "Advisory Fuel Rates").</summary>
    public required string Name { get; init; }

    /// <summary>OR-list of keyword/synonym/alias phrases.</summary>
    public required IReadOnlyList<string> Keywords { get; init; }

    /// <summary>Hard cap on loop passes for this group (default 3, tunable per group).</summary>
    public int MaxLoops { get; init; } = 3;
}
