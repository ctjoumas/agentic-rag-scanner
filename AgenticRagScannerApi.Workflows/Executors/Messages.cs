using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Pipeline;

namespace AgenticRagScannerApi.Workflows.Executors;

// Typed messages that flow along the workflow edges, one hop per step. These carry the data a step
// just produced; the accumulating per-group state (queries, passes, reviews) stays in the injected
// TopicGroupContext.History. New message records are added here as each executor is introduced.

/// <summary>
/// Starts a pass. This is the workflow input that lands on <see cref="QuerySynthesisExecutor"/> for
/// the first pass; the Loop Controller's retry edge re-enters that same executor for later passes.
/// </summary>
public sealed record PassStart;

/// <summary>
/// The synthesized query for the current pass plus the model's brief rationale. Emitted by
/// <see cref="QuerySynthesisExecutor"/> and consumed by the web-search step. Mirrors
/// <see cref="Agents.QuerySynthesisResult"/>, which is also recorded on the pass's
/// <see cref="LoopPass.Query"/>/<see cref="LoopPass.QueryRationale"/>.
/// </summary>
public sealed record QueryResult(string Query, string? Rationale);

/// <summary>
/// The raw web-search results for the current pass. The query is not carried here: it was already
/// recorded on the pass's <see cref="LoopPass.Query"/> by <see cref="QuerySynthesisExecutor"/> and no
/// later step needs it from the message. Emitted by <see cref="WebSearchExecutor"/> and consumed by
/// <see cref="PreFilterExecutor"/>.
/// </summary>
public sealed record HitsResult(IReadOnlyList<SearchHit> Hits);

/// <summary>
/// The deterministically pre-filtered hits for the current pass (valid http(s) URLs, de-duplicated
/// in-group and cross-group). Emitted by <see cref="PreFilterExecutor"/> - which has already appended
/// them to <see cref="LoopPass.Hits"/> - and consumed by the fetch-and-clean step.
/// </summary>
public sealed record FilteredHitsResult(IReadOnlyList<SearchHit> Hits);

/// <summary>
/// The fetched-and-cleaned full-text documents for the current pass, one per filtered hit (a snippet
/// fallback flagged <see cref="FetchedDocument.Unverified"/> rather than dropped). Emitted by
/// <see cref="FetchAndCleanExecutor"/> and consumed by the relevance-eval step. These are transient
/// loop state, so they travel on the edge rather than being recorded on the pass.
/// </summary>
public sealed record DocumentsResult(IReadOnlyList<FetchedDocument> Documents);

/// <summary>
/// The relevance-eval agent's raw per-pass verdict (<see cref="ReviewDecision"/>) alongside the
/// documents it judged. Both travel together because the loop-controller step needs the documents and
/// the decision to build the recorded <see cref="LoopPass.Review"/>. Emitted by
/// <see cref="RelevanceEvalExecutor"/> and consumed by the loop-controller step. Still transient loop
/// state: nothing is written to the pass until the loop controller runs.
/// </summary>
public sealed record EvaluationResult(IReadOnlyList<FetchedDocument> Documents, ReviewDecision Decision);
