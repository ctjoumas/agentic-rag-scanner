# MAF executor design: single-executor pipeline vs. step-per-executor graph

**Status:** Recommended — decompose the loop body into **six per-step executors** (Query Synthesis, Web Search, Pre-filter, Fetch & Clean, Relevance Eval, Loop Controller). No code change yet; tracked as backlog story 12.4 / implementation-plan Phase 12.
**Audience:** Team review.
**Scope:** How we orchestrate one topic group's agentic RAG loop on Microsoft Agent Framework (MAF), and whether to keep the current "pipeline inside one executor" shape or move to the more idiomatic "graph of executors connected by edges."

---

## 1. What we have today

One topic group runs as a **single MAF executor** that self-loops:

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

### Recommended decomposition — six loop-body executors

When we decompose, the **loop body** (everything in `RunPassAsync`) becomes these **six executors**, wired in the frozen order. The loop's **branch lives on the Relevance Eval executor's response**: that executor applies the loop-control rules (max-pass cap, ≥80%-relevant early-exit override) and emits a decision of `Retry` **or** `Finalize`, which two MAF conditional edges route on:

| # | Executor | Kind | Input → output message | Notes |
| --- | --- | --- | --- | --- |
| 1 | `QuerySynthesisExecutor` | LLM (MAF agent) | `PassStart` → `QueryReady` | reads `SearchHistory` to rotate synonyms / avoid redundancy; one query per pass; **loop-back target on `Retry`** |
| 2 | `WebSearchExecutor` | Foundry agent (Grounding w/ Bing Custom Search) | `QueryReady` → `HitsReady` | executes the synthesized query; allowlist-scoped |
| 3 | `PreFilterExecutor` | deterministic code | `HitsReady` → `FilteredHits` | dedupe (incl. earlier passes + cross-group) + URL validity |
| 4 | `FetchAndCleanExecutor` | HTTP | `FilteredHits` → `DocumentsReady` | **fan-out/fan-in per hit** (replaces the sequential `foreach`) |
| 5 | `RelevanceEvalExecutor` | LLM (MAF agent) + loop-control rules | `DocumentsReady` → `ReviewDecision (Decision: LoopDecision)` | full text + dates + history → per-item verdicts; **the branching node**: its `ReviewDecision.Decision` is checked by two conditional edges — `Retry` loops back to (1), `Finalize` exits to the finalize tail |
| 6 | `FinalizeExecutor` | code | `ReviewDecision (Finalize)` → result | runs the existing sequential finalize tail (`VerdictRouting → Enrichment → Categorize → Summarize&Impact`); reached **only** on the `Finalize` edge |

**Scope note — finalize chain stays a tail.** The post-loop chain (`VerdictRouting → Enrichment → Categorize → Summarize&Impact`) is **out of scope** for this decomposition. On the Relevance Eval executor's **Finalize** edge it remains the existing sequential tail (kept as a single `FinalizeExecutor` over today's `FinalizeAsync`). Splitting the finalize chain into per-step executors can follow later if per-item fan-out or per-step telemetry is wanted there too.

**Why the branch lives on the Relevance Eval response.** "Retry vs finalize" is a runtime routing decision made *from* the eval result — the relevance verdicts (plus the pass-count cap and the ≥80% override) are exactly the inputs that decide it. In MAF a fork is expressed as **two outgoing conditional edges from one executor**, each guarded by a `condition: Func<object?, bool>` predicate that inspects that executor's output message. So the Relevance Eval executor emits the existing `ReviewDecision` whose `Decision` is a `LoopDecision` (`Retry` or `Finalize`), one conditional edge loops back to `QuerySynthesisExecutor` (executor #1) on `Retry`, and the other exits to `FinalizeExecutor` on `Finalize`. This executor is also the natural per-pass synchronization / checkpoint owner (where `SearchHistory` is fully updated for the pass), so the per-pass `OnCheckpointingAsync` / `OnCheckpointRestoredAsync` live here. A separate "loop controller" node is unnecessary — the loop-control logic folds into the eval step and the fork is the conditional edges themselves.

### Suggested decision

1. Ship the current single-executor design for this phase.
2. Open an epic: *"Decompose topic-group pass into per-step executors (mid-pass checkpointing + fan-out)."* — the **six** loop-body executors above (story 12.4).
3. Sequence it with / before Epic 12 (parallel fan-out), since they share the decomposition work.

---

## 7. Sketch of the target (for discussion only)

Rough executor shapes (names illustrative):

```csharp
// Each step is its own executor; messages are the typed payloads between steps.
sealed class QuerySynthesisExecutor   : Executor<PassStart, QueryReady> { /* LLM */ }
sealed class WebSearchExecutor        : Executor<QueryReady, HitsReady> { /* Bing */ }
sealed class PreFilterExecutor        : Executor<HitsReady, FilteredHits> { /* code */ }
sealed class FetchAndCleanExecutor    : Executor<FilteredHits, DocumentsReady> { /* fan-out per hit */ }
// Relevance Eval runs IRelevanceEvalAgent.EvaluateAsync and applies the loop-control rules
// (recall override + maxLoops cap) so the emitted ReviewDecision.Decision is the *final* decision.
sealed class RelevanceEvalExecutor    : Executor<DocumentsReady, ReviewDecision> { /* LLM + loop rules */ }
sealed class FinalizeExecutor         : Executor<ReviewDecision> { /* VerdictRouting → … → result */ }
```

The decision the conditional edges route on is the existing `LoopDecision` enum, carried on the eval agent's existing `ReviewDecision` output — no new types are needed:

```csharp
// Existing contracts (AgenticRagScannerApi.Core.Contracts):
//   enum LoopDecision { Retry, Finalize }
//   sealed class ReviewDecision { string ThoughtProcess; LoopDecision Decision; IReadOnlyList<ItemVerdict> Items; }
// IRelevanceEvalAgent.EvaluateAsync(...) returns ReviewDecision.

// Type-safe condition factory, per the MAF conditional-edges pattern.
static Func<object?, bool> When(LoopDecision expected) =>
    msg => msg is ReviewDecision r && r.Decision == expected;
```

```csharp
var builder = new WorkflowBuilder(querySynthesis)
    .AddEdge(querySynthesis, webSearch)
    .AddEdge(webSearch, preFilter)
    .AddEdge(preFilter, fetchAndClean)     // fan-out happens inside / via per-item activations
    .AddEdge(fetchAndClean, relevanceEval) // fan-in before eval
    // The branch is checked on the Relevance Eval executor's ReviewDecision response:
    .AddEdge(relevanceEval, querySynthesis, condition: When(LoopDecision.Retry))     // Retry → back to executor #1 for another pass
    .AddEdge(relevanceEval, finalize,       condition: When(LoopDecision.Finalize))  // Finalize → exit to the finalize tail
    .WithOutputFrom(finalize);
```

> **Decision precedence.** Today the eval agent emits the *raw* decision (`ReviewDecision.Decision` → `Review.LlmDecision`) and the **LoopController** applies the recall override + `maxLoops` cap to produce `Review.FinalDecision`, which `TopicGroupContext.ShouldContinue()` reads. In the decomposed design those rules fold into `RelevanceEvalExecutor`, so the `ReviewDecision` it emits already carries the **final** `LoopDecision` the conditional edges route on (at the cap it is always `Finalize`).

`SearchHistory` would still be checkpointed, but per-step intermediate payloads (hits, filtered, documents) would also be checkpointable, enabling resume between steps.

> This section is a thinking aid, not a committed API. Exact MAF signatures, fan-out mechanics, and conditional-edge syntax should be validated against MAF 1.10 before implementation.
