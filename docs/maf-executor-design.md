# MAF executor design: single-executor pipeline vs. step-per-executor graph

**Status:** ✅ Implemented (phase-7) — the loop body is decomposed into **seven executors** (Query Synthesis, Web Search, Pre-filter, Fetch & Clean, Relevance Eval, Loop Controller, Finalize). The monolithic `TopicGroupLoopExecutor` and the `PassSignal` enum have been removed.
**Audience:** Team review.
**Scope:** How we orchestrate one topic group's agentic RAG loop on Microsoft Agent Framework (MAF), and whether to keep the original "pipeline inside one executor" shape or move to the more idiomatic "graph of executors connected by edges."

> **Implementation note (as-built).** Sections 1–6 below are the original decision record (kept for context); the recommendation was adopted. Two details changed during implementation and are corrected in place:
> - **Message records** use a `…Result` suffix: `PassStart`, `QueryResult`, `HitsResult`, `FilteredHitsResult`, `DocumentsResult`, `EvaluationResult` (the last wraps the documents + the raw `ReviewDecision`), then the domain `Review`. They live in `Executors/Messages.cs`.
> - **Checkpoint ownership** is a *single owner* on `QuerySynthesisExecutor`, **not** the Loop Controller — see the corrected explanation in §6 and the mechanism in §3.1.

---

## 1. The original design (historical — replaced in phase-7)

> This section describes the single-executor shape that shipped first and was **removed** in phase-7. It's retained to explain what the decomposition replaced.

One topic group ran as a **single MAF executor** that self-looped:

- `TopicGroupLoopExecutor : Executor<PassSignal>` is the only node in the workflow graph.
- The graph has exactly one edge: a **self-edge** that carries `PassSignal.Continue` to drive the next loop pass. (`TopicGroupWorkflow.Build`.)
- All ten agents/steps run as plain, sequential `await` calls **inside** `TopicGroupPipeline.RunPassAsync` / `FinalizeAsync` — MAF never sees them as nodes.

```
RunPassAsync (one pass):
  1. QuerySynthesis  (LLM)     → one query
  2. WebSearch       (Bing)    → hits
  3. PreFilter       (code)    → dedupe / cross-group / URL validity
  4. Fetch & Clean   (HTTP)    → loop: one fetch per hit   ← sequential foreach
  5. RelevanceEval   (LLM)     → per-item verdicts
  6. LoopController  (code/LLM)→ retry or finalize

FinalizeAsync (once, on exit):
  VerdictRouting → Enrichment → Categorize → Summarize&Impact   (per carried item)
```

The executor is intentionally a **thin adapter** over `TopicGroupPipeline`. This was a deliberate choice: the loop is unit-testable as plain C# without standing up a workflow.

### How this maps to MAF's execution model

MAF runs in **super-steps**. For our single self-looping executor:

> **1 super-step = 1 invocation of `HandleAsync` = 1 full pass.**

At the end of each super-step, MAF takes a checkpoint. Our executor contributes its in-memory `SearchHistory` to that checkpoint via `OnCheckpointingAsync` (serialized as JSON under the `"SearchHistory"` state key), and restores it on resume via `OnCheckpointRestoredAsync`. The checkpoint is persisted to Cosmos through `CosmosCheckpointStore`.

---

## 2. The other pattern: a graph of executors

The canonical MAF shape decomposes each step into its **own** `Executor`, wired together with `AddEdge(...)`. Typed messages flow along edges (query → hits → filtered → documents → decision). The agentic loop becomes a **cyclic graph** with a **conditional edge** out of the loop controller:

```mermaid
flowchart LR
    QS[QuerySynthesis] --> WS[WebSearch]
    WS --> PF[PreFilter]
    PF -->|fan-out per hit| FC[Fetch & Clean]
    FC -->|fan-in| RE[RelevanceEval]
    RE --> LC[LoopController]
    LC -->|Continue| QS
    LC -->|Finalize| VR[VerdictRouting]
    VR --> EN[Enrichment]
    EN --> CAT[Categorize]
    CAT --> SUM[Summarize & Impact]
    SUM --> OUT([Yield TopicGroupResult])
```

Here the loop-back ("re-synthesize on the next pass") is just an **edge** from `LoopController` back to `QuerySynthesis`, instead of a `PassSignal.Continue` self-message wrapping an internal pipeline.

Both patterns are fully supported and idiomatic in MAF. Decomposition is **not** required.

---

## 3. Checkpointing — the core of the question

**Question raised:** *"Right now it saves a loop pass and resumes from there — so if it goes through one full pass (query synth → Bing → save docs → eval) and then it failed, it would not repeat that pass but pick up on the next pass?"*

**Answer: partly — and the caveat is the whole point.**

The checkpoint boundary is the **completed pass**, not the steps within it. So the behavior depends on *when* the failure happens:

| Failure timing | What happens on resume |
| --- | --- |
| Pass **N completes** (super-step ends, checkpoint written), then failure occurs while starting/processing pass **N+1** | Resume restores the post-pass-N checkpoint and **re-runs pass N+1 from the top**. Pass N is **not** repeated. ✅ This is the case you described. |
| Failure happens **mid-pass N** (e.g., Bing times out at step 2, or the LLM errors during eval at step 5) | The super-step never completed, so **no checkpoint for pass N was written**. Resume replays **all of pass N from step 1** — re-running query synthesis, Bing, *all* the fetches, and eval that already succeeded earlier in that same pass. ❌ |

So: **you can resume between passes, but you cannot resume within a pass.** Because most real failures (network timeouts, throttling, transient LLM errors) happen *during* a pass — often at the most expensive step (Fetch & Clean or RelevanceEval) — the practical effect is that a failure usually costs you the **entire** pass's LLM + Bing + HTTP work, not just the failed step.

### What step-per-executor checkpointing would give us

If each step were its own executor, MAF would checkpoint at **each step boundary** (each step is its own super-step). Then:

- A failure during RelevanceEval would resume **after** Fetch & Clean, replaying only the eval — **not** re-paying for the query, the Bing call, and all the document fetches.
- This is the single strongest argument for decomposing: **mid-pass resumability** on an expensive, network-bound agentic loop.

> Net: today's design = "checkpoint per pass." Decomposed graph = "checkpoint per step." For a loop that makes multiple paid LLM/Bing/HTTP calls per pass, per-step is materially more resilient and cheaper to recover.

### 3.1 How per-step resume actually works (as-built)

The decomposed graph delivers the mid-pass resume promised above, but the mechanism is subtle and worth spelling out, because it rests on **two facts working together**:

1. **There is exactly one `SearchHistory`, shared by all seven executors.** `TopicGroupContext` is injected (by reference) into every executor's constructor via `ActivatorUtilities.CreateInstance<T>(serviceProvider, context)`, so they all hold the *same* instance. When `PreFilterExecutor` appends hits or `LoopControllerExecutor` records the pass `Review`, it mutates the same object `QuerySynthesisExecutor` references. The loop's accumulating state lives in one place no single executor "owns."
2. **MAF invokes `OnCheckpointingAsync` on *every* executor at *every* super-step checkpoint** — not only the executor that ran that step. (We confirmed this empirically: having all seven write the same state key threw `InvalidOperationException: Expected exactly one update for key 'SearchHistory'`.)

Put together: a **single checkpoint owner** is enough. `QuerySynthesisExecutor` carries the only `OnCheckpointingAsync` / `OnCheckpointRestoredAsync` hooks. Its save hook fires at the end of *every* super-step (not just step 1), each time snapshotting whatever the shared `SearchHistory` currently holds — so the checkpoint taken after step 4 already contains the in-progress pass *and* its hits. On resume, its restore hook rehydrates that one shared object, and because every other executor references the same instance, they all immediately see the restored state. One writer, one reader — and **multiple writers would simply collide on the key**, which is why the other six executors deliberately have no hooks.

The history is written to a **named (`"shared"`) state scope** rather than an executor's private scope. A named scope is readable across executors (MAF's `ScopeKey` treats keys that differ only by `ExecutorId` as equal), which keeps the design honest even though, in-process, the shared object already does the heavy lifting. The serialize/restore logic is centralized in the static `SearchHistoryCheckpoint` helper.

---

## 4. Other benefits of the graph approach

Beyond checkpointing:

- **Fan-out / fan-in concurrency.** The Fetch & Clean step is currently a sequential `foreach (var hit in filtered) await _fetchAndClean.FetchAsync(...)`. As executors, Fetch & Clean (and per-doc eval) become a **fan-out**: N documents processed concurrently, joined before the controller. This is a natural MAF strength we currently leave unused.
- **Per-step observability.** Each executor emits its own super-step events, so we get a real per-node trace (latency, retries, failures per step) instead of an opaque "pass." The workflow can also be visualized.
- **Per-step retry / policy.** Resilience policies (retry, backoff) can be attached per step, where they belong (e.g., retry Bing, but don't retry a deterministic filter).
- **Composability.** Steps become reusable nodes that can be rewired (e.g., swap the finalize chain) without touching the loop body.

---

## 5. Costs / why it isn't free

- **Shared mutable state → typed messages.** The pipeline leans on mutating `TopicGroupContext` / `SearchHistory` in place. The edge-based model wants **typed messages passed between executors**. We'd either thread those as messages (idiomatic, more refactor) or keep mutating shared `context` (works in-process, but forfeits much of the benefit and is less clean). This is the main cost.
- **Loses some test simplicity.** The current selling point — "unit-test the loop without a workflow" — partly goes away; more behavior would be tested through MAF.
- **More boilerplate + more checkpoint state keys** to serialize between steps.
- **Loop-back + shared per-pass state** (e.g., the `LoopPass` being appended and then enriched within a pass) needs careful modeling so cross-step state survives edges and checkpoints.

---

## 6. Recommendation

- **It is not wrong to ship what we have.** It's the legitimate "code orchestration" pattern: simpler, well-documented, unit-testable. Fine for the POC and the current phase.
- **If we want mid-pass resume, parallel fetch/eval, and per-step telemetry**, decompose the pass into per-step executors with a conditional loop-back edge from `LoopController`. That matches the canonical MAF design and our original intuition.
- **Timing:** This is a meaningful refactor (mostly the shared-`context` → typed-message change), so it should **not** be folded into the current PR. It's a clean candidate for its own epic, and it **pairs naturally with parallel fan-out (Epic 12)** — the same decomposition that parallelizes topic groups also parallelizes fetch/eval *within* a group.

### Recommended decomposition — seven executors (six loop-body + finalize tail)

When we decompose, the **loop body** (everything in `RunPassAsync`) becomes **six executors** plus a **`FinalizeExecutor`** tail, wired in the frozen order. The loop's **branch lives on the Loop Controller executor's response**: `RelevanceEvalExecutor` emits the *raw* eval verdicts, and `LoopControllerExecutor` applies the loop-control rules (max-pass cap, ≥80%-relevant early-exit override), maps verdicts to vetted/discarded items (persisting carried full text to blob), and emits the existing `Review` whose `FinalDecision` is `Retry` **or** `Finalize` — which two MAF conditional edges route on:

| # | Executor | Kind | Input → output message | Notes |
| --- | --- | --- | --- | --- |
| 1 | `QuerySynthesisExecutor` | LLM (MAF agent) | `PassStart` / `Review` → `QueryResult` | reads `SearchHistory` to rotate synonyms / avoid redundancy; one query per pass; **loop-back target on `Retry`** (re-entered with the controller's `Review`); **sole checkpoint owner** (see §3.1) |
| 2 | `WebSearchExecutor` | Foundry agent (Grounding w/ Bing Custom Search) | `QueryResult` → `HitsResult` | executes the synthesized query; allowlist-scoped |
| 3 | `PreFilterExecutor` | deterministic code | `HitsResult` → `FilteredHitsResult` | dedupe (incl. earlier passes + cross-group) + URL validity; **sole writer of the pass's hits** |
| 4 | `FetchAndCleanExecutor` | HTTP | `FilteredHitsResult` → `DocumentsResult` | sequential per hit today (fan-out/fan-in is a later step) |
| 5 | `RelevanceEvalExecutor` | LLM (MAF agent) | `DocumentsResult` → `EvaluationResult` | full text + dates + history → per-item verdicts + the *raw* `ReviewDecision`; documents ride along in the message; no loop-control rules here |
| 6 | `LoopControllerExecutor` | deterministic code | `EvaluationResult` → `Review` | **the branching node**: applies the `maxLoops` cap + ≥80% recall override to produce `Review.FinalDecision`, maps verdicts to vetted/discarded items (persists carried full text to blob); its `Review.FinalDecision` is checked by two conditional edges — `Retry` loops back to (1), `Finalize` exits to the finalize tail |
| 7 | `FinalizeExecutor` | code | `Review` → yields `TopicGroupResult` | runs the existing sequential finalize tail (`VerdictRouting → Enrichment → Categorize → Summarize&Impact`); reached **only** on the `Finalize` edge; terminal node, so it `YieldOutputAsync`es the result rather than emitting an edge message |

**Scope note — finalize chain stays a tail.** The post-loop chain (`VerdictRouting → Enrichment → Categorize → Summarize&Impact`) is **out of scope** for this decomposition. On the Loop Controller executor's **Finalize** edge it remains the existing sequential tail (kept as a single `FinalizeExecutor` over today's `FinalizeAsync`). Splitting the finalize chain into per-step executors can follow later if per-item fan-out or per-step telemetry is wanted there too.

**Why the branch lives on the Loop Controller response.** "Retry vs finalize" is a runtime routing decision that needs state a stateless edge predicate can't see: the pass count and `maxLoops` cap plus the ≥80% recall override. It also has side effects (mapping verdicts to vetted/discarded items and persisting carried full text to blob). MAF edge conditions are stateless `Func<object?, bool>` predicates over the source executor's *output message* only, so the decision logic must live in an executor and the "conditional" is the edges reading its output. We keep the existing `LoopController` as that executor: `RelevanceEvalExecutor` emits the raw `ReviewDecision` (wrapped in `EvaluationResult` alongside the documents), `LoopControllerExecutor` runs today's `ReviewPassAsync` (cap + override + item routing + blob persistence) and emits the existing `Review` carrying the final decision. In MAF a fork is **two outgoing conditional edges from one executor**, each guarded by a `condition: Func<T, bool>` predicate over its output: one loops back to `QuerySynthesisExecutor` (executor #1) when `Review.FinalDecision == Retry`, the other exits to `FinalizeExecutor` when `Review.FinalDecision == Finalize`. Keeping it a distinct node (rather than folding the rules into the eval step) preserves the existing separation of concerns and the unit-testable `ILoopController`.

> **Correction (as-built): checkpoint owner is Query Synthesis, not the Loop Controller.** This doc originally proposed putting the per-pass `OnCheckpointingAsync` / `OnCheckpointRestoredAsync` on the Loop Controller as "the natural per-pass synchronization point." That isn't how the shared-state checkpoint works (see §3.1): because `SearchHistory` is one object shared by all executors and MAF fires the checkpoint hook on *every* executor each super-step, exactly **one** executor must write the shared key or the writes collide. We put that single owner on `QuerySynthesisExecutor` (the always-present start node); its save hook still captures the fully-updated history at every super-step boundary, including after the Loop Controller runs.

### Suggested decision

1. ~~Ship the current single-executor design for this phase.~~ Shipped, then superseded.
2. ~~Open an epic: *"Decompose topic-group pass into per-step executors (mid-pass checkpointing + fan-out)."*~~ **✅ Done (phase-7):** the **seven** executors above are implemented, the monolith + `PassSignal` removed, and mid-pass resume is proven by `WorkflowResumeTests` (resumes from a mid-pass checkpoint and completes).
3. Fan-out/fan-in *within* `FetchAndCleanExecutor` (and per-doc eval) remains future work and still pairs naturally with Epic 12 (parallel topic groups).

---

## 7. Sketch of the target (for discussion only)

Rough executor shapes (names illustrative):

```csharp
// Each step is its own executor; messages are the typed payloads between steps.
sealed class QuerySynthesisExecutor   : Executor              { /* PassStart|Review → QueryResult; LLM; sole checkpoint owner */ }
sealed class WebSearchExecutor        : Executor<QueryResult, HitsResult> { /* Bing */ }
sealed class PreFilterExecutor        : Executor<HitsResult, FilteredHitsResult> { /* code */ }
sealed class FetchAndCleanExecutor    : Executor<FilteredHitsResult, DocumentsResult> { /* per hit */ }
// Relevance Eval runs IRelevanceEvalAgent.EvaluateAsync and emits the *raw* verdicts + documents.
sealed class RelevanceEvalExecutor    : Executor<DocumentsResult, EvaluationResult> { /* LLM */ }
// Loop Controller runs today's ILoopController.ReviewPassAsync (recall override + maxLoops cap +
// item routing + blob persistence) so the emitted Review.FinalDecision is the *final* decision.
sealed class LoopControllerExecutor   : Executor<EvaluationResult, Review> { /* code + loop rules */ }
sealed class FinalizeExecutor         : Executor<Review> { /* VerdictRouting → … → YieldOutputAsync(result) */ }
```

The decision the conditional edges route on is the existing `LoopDecision` enum, carried on the eval agent's existing `ReviewDecision` output — no new types are needed:

```csharp
// Existing contracts (AgenticRagScannerApi.Core.Contracts / .Runtime):
//   enum LoopDecision { Retry, Finalize }
//   sealed class ReviewDecision { string ThoughtProcess; LoopDecision Decision; IReadOnlyList<ItemVerdict> Items; }
//   sealed class Review { LoopDecision LlmDecision; LoopDecision FinalDecision; ... }
// IRelevanceEvalAgent.EvaluateAsync(...) returns ReviewDecision; ILoopController.ReviewPassAsync(...) produces the Review.

// Type-safe condition factory, per the MAF conditional-edges pattern — routes on the
// Loop Controller's Review.FinalDecision (cap + recall override already applied).
static Func<object?, bool> When(LoopDecision expected) =>
    msg => msg is Review r && r.FinalDecision == expected;
```

```csharp
var builder = new WorkflowBuilder(querySynthesis)
    .AddEdge(querySynthesis, webSearch)
    .AddEdge(webSearch, preFilter)
    .AddEdge(preFilter, fetchAndClean)
    .AddEdge(fetchAndClean, relevanceEval)
    .AddEdge(relevanceEval, loopController)  // EvaluationResult (raw decision + docs) → cap + override → Review
    // The branch is checked on the Loop Controller executor's Review.FinalDecision response:
    .AddEdge<Review>(loopController, querySynthesis, condition: r => r.FinalDecision == LoopDecision.Retry)     // → another pass
    .AddEdge<Review>(loopController, finalize,       condition: r => r.FinalDecision == LoopDecision.Finalize)  // → finalize tail
    .WithOutputFrom(finalize);
```

> **Decision precedence.** Today the eval agent emits the *raw* decision (`ReviewDecision.Decision` → `Review.LlmDecision`) and the **LoopController** applies the recall override + `maxLoops` cap to produce `Review.FinalDecision`, which `TopicGroupContext.ShouldContinue()` reads. The decomposed design keeps that split: `RelevanceEvalExecutor` emits the raw `ReviewDecision`, and `LoopControllerExecutor` produces the `Review` whose `FinalDecision` the conditional edges route on (at the cap it is always `Finalize`).

`SearchHistory` would still be checkpointed, but per-step intermediate payloads (hits, filtered, documents) would also be checkpointable, enabling resume between steps.

> This section is a thinking aid, not a committed API. Exact MAF signatures, fan-out mechanics, and conditional-edge syntax should be validated against MAF 1.10 before implementation.
