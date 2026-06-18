using AgenticRagScannerApi.Core.Contracts;

namespace AgenticRagScannerApi.Core.Runtime;

/// <summary>
/// Per-topic-group, in-memory loop state for the duration of a run (NOT persisted).
/// <see cref="Passes"/> is the single source of truth; the flat views (<see cref="Queries"/>,
/// <see cref="Reviews"/>, <see cref="Decisions"/>, <see cref="Vetted"/>, <see cref="Discarded"/>)
/// are projections over it, so they can never drift out of sync.
/// </summary>
public sealed class SearchHistory
{
    /// <summary>Every pass, in order. The source of truth for the loop's history.</summary>
    public List<LoopPass> Passes { get; } = [];

    /// <summary>Normalized URLs already seen this run; powers the deterministic pre-filter dedupe.</summary>
    public HashSet<string> ProcessedKeys { get; } = [];

    /// <summary>The in-progress (most recent) pass, or null before the first pass.</summary>
    public LoopPass? CurrentPass => Passes.Count > 0 ? Passes[^1] : null;

    /// <summary>Every query tried so far (fed to the query-synthesis agent to avoid repeats).</summary>
    public IEnumerable<string> Queries => Passes.Select(p => p.Query);

    /// <summary>Review reasoning per reviewed pass.</summary>
    public IEnumerable<string> Reviews =>
        Passes.Where(p => p.Review is not null).Select(p => p.Review!.ThoughtProcess);

    /// <summary>Final decision per reviewed pass.</summary>
    public IEnumerable<LoopDecision> Decisions =>
        Passes.Where(p => p.Review is not null).Select(p => p.Review!.FinalDecision);

    /// <summary>All vetted items across passes (Relevant/Borderline carried forward).</summary>
    public IEnumerable<ResultItem> Vetted =>
        Passes.Where(p => p.Review is not null).SelectMany(p => p.Review!.Vetted);

    /// <summary>All discarded items across passes (NotRelevant, kept for audit).</summary>
    public IEnumerable<ResultItem> Discarded =>
        Passes.Where(p => p.Review is not null).SelectMany(p => p.Review!.Discarded);
}
