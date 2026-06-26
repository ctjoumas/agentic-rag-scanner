# Agentic RAG Scanner — Sprint Backlog (Epics & User Stories)

> Companion to `docs/implementation-plan.md`. Each **Epic = a phase**; each **story** is a
> board-ready issue with acceptance criteria, a suggested lane owner, labels, and dependencies.
> Transcribe directly onto the GitHub Project board, or create with `gh issue create` (see bottom).
>
> **Lanes:** **L1** Orchestration & Workflow · **L2** AI Agents & Grounding · **L3** Data, Quality & Platform.
> **Global DoD** (`implementation-plan.md §6`) applies to *every* story on top of its own criteria.
>
> **Note on IDs:** some story numbers are intentionally skipped where small stories were merged
> (e.g. `0.6`, `1.4`), and the **former `phase-2`** (background execution & run status) was **merged into `phase-1`**, with later phases **renumbered to stay contiguous**.
> IDs are **stable references** for the board, not a contiguous sequence; merged stories are annotated *(merged: former X + Y)*.

---

## Suggested labels & milestones

**Milestones (one per phase):** `phase-0` … `phase-13`.

**Type labels:** `epic`, `user-story`, `task`, `spike`, `chore`, `bug`.
**Lane labels:** `lane:L1-orchestration`, `lane:L2-agents`, `lane:L3-data-platform`.
**Area labels:** `area:maf`, `area:llm`, `area:bing`, `area:cosmos`, `area:storage`,
`area:search`, `area:observability`, `area:security`, `area:ci`, `area:docs`.
**Misc:** `good-first-issue`, `blocked`, `needs-design`, `future`.

> Tip: use the milestone for the phase and **both** a `lane:*` and an `area:*` label so the board can
> be filtered per engineer and per concern.

---

## ✅ Epic 0 — Foundations & contracts (interface freeze) · `phase-0` · **Complete**
> Establish shared contracts + cross-cutting scaffolding so the 3 lanes can diverge safely. **Do first.**

### 0.1 — Add `AgenticRagScanner.Core` project · `lane:L3-data-platform`
**As an** engineer, **I want** a shared Core project, **so that** domain models/interfaces live in one
place all projects reference.
**AC:** Core project added to the solution and referenced by the API; builds; namespaces agreed.
`labels: user-story, area:docs` · **blocks:** all other Phase 0 stories.

### 0.2 — Test project + CI pipeline · `lane:L3-data-platform`
**As an** engineer, **I want** an xUnit project running in CI, **so that** every PR is built, tested, and format-checked.
**AC:**
- `AgenticRagScanner.Tests` (xUnit) added with one trivial passing test.
- GitHub Actions PR workflow runs `dotnet build`, `dotnet test`, and `dotnet format --verify-no-changes`.
- Workflow is a **required status check** on `main`.
`labels: user-story, area:ci` · **depends on:** 0.1 · *(merged: former 0.2 + 0.6)*

### 0.3 — Define core domain contracts (the interface freeze) · `lane:L2-agents` *(co-review: all)*
**As a** team, **we want** frozen shared contracts, **so that** lanes build in parallel without collisions.
**AC:**
- `TopicGroup` (keyword/synonym OR-list + per-group `MaxLoops`, default 3), `RunContext`, `TopicGroupContext`.
- `SearchHistory` (in-memory): `SearchQueries[]`, `VettedResults[]`, `DiscardedResults[]`.
- `ResultItem` schema: source URL(s), `WhatItDoes`, impact area, regulator, tags, `Verdict`,
  `PublicationDate`, `EffectiveDate`, `AppliesFrom`, `AppliesTo`, `DateConfidence`, level-of-authority,
  `Unverified`, `FullTextBlobUri` (blob reference to the persisted cleaned full text — Epic 5),
  `RunId`, `GroupId`, `Version`.
- `Verdict` and `LevelOfAuthority` enums.
- Reviewed & approved by all 3 engineers before merge.
`labels: user-story, needs-design` · **depends on:** 0.1 · **blocks:** Phases 1–10.

### 0.4 — Shared throttle abstraction · `lane:L1-orchestration`
**AC:** `ISharedThrottle`/rate-limiter interface in Core (no real limits yet); unit-testable; DI-registered.
`labels: user-story, area:maf` · **depends on:** 0.1.

### 0.5 — Cross-cutting skeleton: OTel + options validation + health + identity · `lane:L3-data-platform`
**AC:** OpenTelemetry **deferred** (see note); `*Options` use `ValidateDataAnnotations()` +
`ValidateOnStart()`; `/health` endpoint healthy; `DefaultAzureCredential` registered.
> **Note (deferred):** OpenTelemetry skeleton deferred - the app already ships Serilog -> Console + App Insights for structured logging; OTel traces/metrics will be added in a dedicated observability step once agents emit token/latency/verdict telemetry (Phase 3+).
`labels: user-story, area:observability` · **depends on:** 0.1.

### 0.7 — Chore: genericize customer material in design doc · `lane:L3-data-platform`
**AC:** "Reg Advantage" -> generic term; `Topic Groups.docx` reference genericized; no customer-identifying
text remains (primer §1).
`labels: chore, area:docs, good-first-issue`.

---

## ✅ Epic 1 — Run lifecycle: synchronous scan · `phase-1` · *L1-led* · **Complete**
> Synchronous request/response for the POC — no long-running/async machinery. *(Former Epic 2 —
> background execution & run status — merged here; the async path is captured in Epic 13.)*

### 1.1 — Scan orchestrator: synchronous sequential execution + controller trigger · `lane:L1-orchestration`
**As an** auditor, **I want** a scan to start from the API **and return its results in the response**, **so that** I get an answer for each selected topic group in one call.
**AC:**
- `IScanOrchestrator` maps `ScanRequest` -> one `TopicGroupContext` per group (each seeded with an empty `SearchHistory`), then runs them **sequentially, one group at a time** (parallel fan-out is deferred to Epic 12).
- `ScannerController.Scan` calls the orchestrator (replaces the current TODO), **runs the scan synchronously and returns the aggregated results (`200`)** — no run-status/polling for the POC.
- Shared throttle wired for outbound LLM/Bing **rate-limiting** (0.4).
`labels: user-story, area:maf` · **depends on:** 0.3, 0.4 · *(merged: former 1.1 + 1.5 + Epic 2's sequential loop)*

### 1.2 — Per-group execution step (walking skeleton -> MAF) · `lane:L1-orchestration`
**AC:** each group runs a stub step returning a placeholder result **synchronously** (**Epic 2 replaces it with the real MAF workflow**); per-group progress + spans observable in logs (`runId`/`topicGroupId`).
`labels: user-story` · **depends on:** 1.1.

**Epic demo:** `POST scan` (3 groups) -> `200` with aggregated stub results; the 3 groups are processed **one after another**; logs show per-group progress. No status endpoint, no MAF yet.

---

## ✅ Epic 2 — MAF workflow scaffolding (stub agents) · `phase-2` · *L1 + L2* · **Complete**
> All agents + steps present but **stubbed**. **Sync point:** agent I/O contracts frozen here.
> **Agent hosting:** Query Synthesis and the four downstream agents (Eval, Enrichment, Categorize, Summarize) are **MAF agents over a Foundry model deployment**; the **Web Search agent** is a **pre-provisioned Foundry agent** (created in the Foundry portal with the Grounding with Bing Custom Search tool) the MAF workflow **resolves by name and runs** — it **executes the queries** synthesized by the Query Synthesis MAF agent.

### 2.1 — `AgenticRagScanner.Workflows` + one MAF workflow per group + Cosmos checkpointing · `lane:L1-orchestration`
**AC:** Workflows project added; one MAF workflow per topic group; **MAF Cosmos checkpointing** wired to
the shared dev Cosmos account (`checkpoints` container); a run is resumable from checkpoint.
`labels: user-story, area:maf, area:cosmos` · **depends on:** 1.1 · `needs-design` (confirm MAF checkpoint API).

### 2.2 — Loop scaffold threading `SearchHistory` · `lane:L1-orchestration`
**AC:** ordered loop wired: QuerySynthesis (MAF agent) -> WebSearch (Foundry agent w/ Grounding with Bing Custom Search) -> Pre-filter -> Fetch&Clean -> RelevanceEval ->
LoopController -> VerdictRouting -> Enrichment -> Categorize -> Summarize&Impact; `SearchHistory` passed each pass.
`labels: user-story, area:maf` · **depends on:** 2.1.

### 2.3 — Loop Controller + Verdict Routing stubs (deterministic) · `lane:L1-orchestration`
**AC:**
- Loop Controller stub honors per-group `maxLoops` (default 3); appends each pass to `SearchHistory`.
- Verdict Routing stub: RELEVANT/BORDERLINE -> enrichment; NOT_RELEVANT -> dropped + logged for audit.
`labels: user-story` · **depends on:** 2.2 · *(merged: former 2.3 + 2.4)*

### 2.4 — Stub the Web Search agent (Foundry agent w/ Grounding with Bing Custom Search tool) · `lane:L2-agents`
**AC:** the **Web Search agent** is the solution's single **Foundry agent** with a **Grounding with Bing Custom Search** tool that **executes the synthesized queries** from the Query Synthesis MAF agent; returns canned grounded hits; allowlist hook present. **Distinct node** between Query Synthesis and Pre-filter.
`labels: user-story, area:bing` · **depends on:** 2.2.

### 2.5 — Stub: Query Synthesis Agent (MAF agent) · `lane:L2-agents`
**AC:** **MAF agent** definition (over the Foundry model deployment) + DI + `Prompts/QuerySynthesisPrompt.cs` placeholder; returns 1–2 canned **queries** (no Bing call).
`labels: user-story, area:llm` · **depends on:** 0.3, 2.2.

### 2.6 — Stub: Relevance Eval Agent · `lane:L2-agents`
**AC:** returns canned `Verdict` + date fields; **MAF agent def (over a Foundry model deployment)** + DI + prompt placeholder.
`labels: user-story, area:llm` · **depends on:** 0.3, 2.2.

### 2.7 — Stub: Enrichment Agent · `lane:L2-agents`
**AC:** returns canned `whatItDoes` + metadata; **MAF agent def (over a Foundry model deployment)** + DI + prompt placeholder.
`labels: user-story, area:llm` · **depends on:** 0.3, 2.2.

### 2.8 — Stub: Categorize Agent · `lane:L2-agents`
**AC:** returns canned impact area / regulator / approved tags; **MAF agent def (over a Foundry model deployment)** + DI + prompt placeholder.
`labels: user-story, area:llm` · **depends on:** 0.3, 2.2.

### 2.9 — Stub: Summarize & Impact Agent · `lane:L2-agents`
**AC:** returns canned plain-English summary; **MAF agent def (over a Foundry model deployment)** + DI + prompt placeholder.
`labels: user-story, area:llm` · **depends on:** 0.3, 2.2.

**Epic demo:** full loop runs end-to-end on fake data, loops to `maxLoops`, routes verdicts, emits stub
`ResultItem`s, **checkpoints to Cosmos**. No external LLM/Bing calls.

---

## ✅ Epic 3 — Foundry model deployment + Query Synthesis Agent (first real agent) · `phase-3` · *L2-led* · **Complete**

### 3.1 — Implement `IFoundryService` (Foundry project + model deployment) · `lane:L2-agents`
**AC:** calls a **Microsoft Foundry project + model deployment** via `DefaultAzureCredential` (prefer `IChatClient` /
`Microsoft.Extensions.AI`); resilience pipeline + shared throttle applied; token/latency metrics. **This is the chat client the five MAF agents (Query Synthesis/Eval/Enrichment/Categorize/Summarize) reference** — project + deployment only, no hosted agent.
`labels: user-story, area:llm` · **depends on:** 2.7 (or 2.6).

### 3.2 — Prompt management convention (`Prompts/*.cs`) · `lane:L2-agents`
**AC:** documented pattern; `QuerySynthesisPrompt.cs` builds the system prompt via interpolation; versioned.
`labels: user-story, area:llm` · **depends on:** 3.1.

### 3.3 — Query Synthesis Agent (real, MAF agent) · `lane:L2-agents`
**AC:** implemented as a **MAF agent** over the Foundry model deployment; synthesizes focused **query strings** from the keyword set; on re-loop reads `SearchHistory` to rotate
synonyms / avoid redundancy; agent decides query count; structured output + bounded retry on invalid JSON. **Returns queries only — the Web Search agent (Epic 4) runs Bing.**
`labels: user-story, area:llm` · **depends on:** 3.1, 3.2.

**Epic demo:** real non-redundant queries; second loop targets untested synonyms/gaps. (Grounded hits arrive once the Web Search agent is real in Epic 4.)

---

## ✅ Epic 4 — Web Search agent (Foundry, Grounding with Bing Custom Search) + deterministic pre-filter · `phase-4` · *L2 + L1/L3* · **Complete**

### 4.1 — Web Search agent (Foundry agent w/ Grounding with Bing Custom Search), allowlist-scoped · `lane:L2-agents`
**AC:**
- Implement the **Web Search agent** as a **pre-provisioned Foundry agent** (created in the Foundry portal with the **Grounding with Bing Custom Search** tool), **resolved by name** (optionally a pinned version) and run by the MAF workflow; it **executes the Query Synthesis agent's queries** and returns grounded hits/citations. The client-side adapter is `WebSearchAgent : IWebSearchAgent` over the MAF `AIAgent` abstraction — no tool/agent is constructed in code.
- Scope the **Grounding with Bing Custom Search** instance to the **primary-source allowlist** so grounding is allowlist-restricted; verify hits/citations are limited to allowlisted domains.
- Supersedes the standalone `IBingSearchGroundingService` / `IBingCustomSearchGroundingService` — grounding is owned by the Foundry agent's tool.
`labels: user-story, area:bing` · **depends on:** 2.4, 3.3 · *(merged: former 4.1 + 4.2)*

### 4.3 — Deterministic pre-filter (dedupe incl. cross-group + URL validity) · `lane:L3-data-platform`
**AC:** pure, unit-tested functions; cross-group dedupe; unreachable/invalid URLs dropped.
`labels: user-story` · **depends on:** 0.3.

**Epic demo:** the Web Search agent executes the synthesized queries and returns real allowlisted results via Grounding with Bing Custom Search; duplicates (incl. cross-group) removed; dead URLs dropped.

---

## ✅ Epic 5 — Full-text fetch & clean + blob storage · `phase-5` · *L3 + L2* · **Complete**

### 5.1 — `IAzureStorageService.UploadBlobAsync` (real) · `lane:L3-data-platform`
**AC:** BlobServiceClient + `DefaultAzureCredential`; containers from options; returns blob URI.
`labels: user-story, area:storage` · **depends on:** 0.5.

### 5.2 — Fetch & clean HTML/PDF with summary fallback · `lane:L2-agents`
**AC:** fetch HTML/PDF, strip boilerplate; cleaned full text is held **in-memory** and passed to the
Relevance Eval agent (Epic 6); on failure fall back to Bing summary + flag `Unverified` (never drop).
Fetch & clean (Epic 5) **never discards** on relevance — discard happens only at eval (Epic 6).
`labels: user-story` · **depends on:** 4.1.

### 5.3 — SSRF guard on fetch · `lane:L3-data-platform` · *deferred → 11.6*
**Status:** deferred. The fetch targets are the customer's curated primary-source (government) domains,
already gated by Grounding with Bing Custom Search, so the fetch step ships in 5.2 with only basic
hygiene (http/https-only scheme + size/redirect/content-type/timeout caps). A full SSRF guard
(host allowlist enforcement, private/loopback/link-local IP blocking) is a **nice-to-have** tracked as
story **11.6**. `labels: user-story, area:security` · **depends on:** 5.2.

### 5.4 — Persist cleaned full text to blob (mandatory, audit) · `lane:L3-data-platform`
**AC:**
- **Mandatory:** every fetched search-result URL's cleaned full text is persisted to blob (audit/provenance —
  the live URL can change or 404, so we snapshot exactly what eval read).
- Store a **blob reference (path/URI)**, on `ResultItem.FullTextBlobUri`; the container
  stays **private** and a short-lived **user-delegation SAS** is minted on demand (or bytes streamed via
  RBAC) when the artifact is viewed.
- Deterministic, idempotent blob key derived from `runId`/`groupId`/item id (e.g.
  `fulltext/{runId}/{groupId}/{itemId}.txt`) so retries/re-runs don't duplicate.
- The full text is **not** stored inline on the Cosmos doc (2 MB item cap + RU cost on every read); the
  Cosmos `ResultItem` (Epic 8) carries the **reference + `EvalRationale`**, so the reasoning and the exact
  source text can always be viewed together.
`labels: user-story, area:storage, area:security` · **depends on:** 5.1, 5.2.

**Epic demo:** cleaned full-text persisted to blob for every result URL and referenced on the `ResultItem`;
unreachable docs -> `Unverified` via fallback.

---

## ✅ Epic 6 — Relevance eval (3-verdict, date-aware) + real loop controller · `phase-6` · *L2 + L1* · **Complete**
> 6.1, 6.2, 6.5 and the new 6.6 are **done** (real full-text eval + real loop/verdict routing + prose
> loop-feedback steer + per-pass history surfaced on the result/API). **6.4** (verdict-distribution
> metric + golden-set recall harness) is **deferred** and folds into Epic 11's evals/observability.

### 6.1 — Relevance Eval Agent (real, full-text, date-aware) · `lane:L2-agents` · **✅ Done**
**AC:** single full-text call -> `RELEVANT/BORDERLINE/NOT_RELEVANT`; distinguishes publication vs
effective vs tax-year dates -> fills date fields + `DateConfidence`; dates as signal, not hard filter.
`labels: user-story, area:llm` · **depends on:** 3.1, 5.2.

### 6.2 — Loop Controller + Verdict Routing (real) · `lane:L1-orchestration` · **✅ Done**
**AC:**
- Loop Controller: re-loop if under `maxLoops` AND goal unmet, OR override if a pass is **≥80% RELEVANT**; `maxLoops` tunable per topic group; `SearchHistory` updated each pass.
- Verdict Routing: BORDERLINE carried forward but flagged in the data structure; NOT_RELEVANT dropped + logged.
`labels: user-story, area:maf` · **depends on:** 6.1, 2.3 · *(merged: former 6.2 + 6.3)*
> **Note:** the recall override fires at **≥80%** RELEVANT (boundary inclusive, matching the reference
> implementation); the per-group cap check (`LoopCount >= MaxLoops`) is evaluated first and always finalizes.

### 6.4 — Verdict-distribution metric + recall check on golden set · `lane:L3-data-platform` · *deferred → 11.1 / 11.3*
**AC:** verdict mix emitted as a metric; eval-harness recall check runs on the golden dataset.
**Status:** deferred. There is no verdict-distribution *metric* nor a golden dataset / recall
harness in the repo yet (verdict routing only *logs* counts today). Tracked to land with the Epic 11
eval suite + observability dashboards (11.1 / 11.3).
`labels: user-story, area:observability` · **depends on:** 6.1.

### 6.5 — Loop-feedback contract: eval steer → query synthesis (Option A / prose) · `lane:L2-agents` · **✅ Done**
**AC:** the eval emits a next-pass steer that distinguishes **missing facet** (broaden) from
**insufficient evidence on a covered facet** (deepen/pivot to an authoritative primary source).
**Phase 6 ships Option (A):** structured `ThoughtProcess` **prose** — no Core contract change; v4 query
synth already reads `SearchHistory.Reviews`. The query-synthesis prompt MUST be extended with explicit
instructions on the steer's **shape and how to interpret each kind**, and the eval + query-synth prompts
are **versioned together** so they never desync. The structured `SearchDirective` alternative (Option B)
is deferred to Epic 11 as a nice-to-have. (See implementation-plan.md Phase 6.)
`labels: user-story, area:llm` · **depends on:** 6.1, 3.3.

### 6.6 — Surface full per-pass history on the result + API (for a future developer UI) · `lane:L1-orchestration` · **✅ Done**
**As a** developer, **I want** the whole agentic-RAG loop history returned with the run, **so that** a
future UI can replay every pass — query + rationale, the hits retrieved, and the review (thought process,
LLM-vs-final decision, override + reason, and the vetted vs. discarded items with URLs, verdicts,
rationale, and dates).
**AC:**
- `TopicGroupResult.History` (`SearchHistorySnapshot?`, nullable) carries the per-pass snapshot; populated
  by `FinalizeExecutor` on finalize from the in-memory `SearchHistory` (null for the Phase 1 stub).
- The snapshot DTOs (`SearchHistorySnapshot` / `LoopPassSnapshot` / `ReviewSnapshot`) live in **Core** so
  the result can reference them, and are reused by the MAF checkpoint serializer.
- Flows out through the API unchanged: `ScannerController` returns `ScanResult` whose `Groups[].History`
  serializes in the HTTP response (the `DateOnly?` date fields use the registered `DateOnlyJsonConverter`).
`labels: user-story, area:maf` · **depends on:** 6.2.

**Epic demo:** items classified with verdicts + dates; loop exits per rules; BORDERLINE flagged/carried; NOT_RELEVANT logged; the run's response includes the full per-pass history for every topic group.

---

## Epic 7 — Enrichment + Categorize + Summarize/Impact (real) · `phase-7` · *L2-led*

### 7.1 — Enrichment Agent (real) · `lane:L2-agents`
**AC:** `whatItDoes` summary + enriched metadata on carried items.
`labels: user-story, area:llm` · **depends on:** 3.1, 6.2.

### 7.2 — Categorize Agent (real, approved tags only) · `lane:L2-agents`
**AC:** impact area + regulator + tags from the controlled vocabulary only.
`labels: user-story, area:llm` · **depends on:** 7.1.

### 7.3 — Summarize & Impact Agent (real, plain-English) · `lane:L2-agents`
**AC:** RAG over in-memory history; effective-date framing; plain-English impact summary.
`labels: user-story, area:llm` · **depends on:** 7.1.

**Epic demo:** each carried item enriched, categorized with approved tags, with a plain-English impact summary.

---

## Epic 8 — Quality gates + Cosmos persistence · `phase-8` · *L3-led*

### 8.1 — Deterministic quality gates · `lane:L3-data-platform`
**AC:** JSON-schema validation; dedupe vs Cosmos; level-of-authority stamping
(legislation > court ruling > HMRC guidance); bad/dup records rejected.
`labels: user-story, area:cosmos` · **depends on:** 0.3.

### 8.2 — `ICosmosResultStore` (versioned docs, idempotent) · `lane:L3-data-platform`
**AC:** Microsoft.Azure.Cosmos + `DefaultAzureCredential`; one versioned doc per item per run;
partition-key strategy; idempotent upsert (ETag); **same account as MAF checkpoints**, separate `results` container.
`labels: user-story, area:cosmos` · **depends on:** 8.1, 2.1.

**Epic demo:** results persisted as versioned Cosmos docs; re-run does not duplicate; authority stamped.

---

## Epic 9 — Publish & export · `phase-9` · *L3-led*

### 9.1 — Publish view + CSV/Excel export · `lane:L3-data-platform`
**AC:**
- Completed run auto-produces the published-update view (generic publishing target — no customer brand).
- CSV/Excel export generated to blob; endpoint returns a download/link.
`labels: user-story, area:storage` · **depends on:** 8.2, 5.1 · *(merged: former 9.1 + 9.2)*

**Epic demo:** completed run yields a downloadable CSV/Excel of published updates.

---

## Epic 10 — Memory / learnings store (Azure AI Search) · `phase-10` · *L3 + L2* · `future`-leaning

### 10.1 — `IAzureSearchService` for curated learnings · `lane:L3-data-platform`
**AC:** Azure.Search.Documents + `DefaultAzureCredential`; index for learnings; vector/hybrid retrieval.
`labels: user-story, area:search` · **depends on:** 0.5.

### 10.2 — Feed retrieved learnings into synthesis + eval · `lane:L2-agents`
**AC:** learnings retrieved (scoped to jurisdiction + topic group, top-K + recency) and injected into
Query Synthesis + Relevance Eval; distinct from per-run `SearchHistory`.
`labels: user-story, area:llm` · **depends on:** 10.1, 3.3, 6.1.

**Epic demo:** prior-run learnings retrieved and demonstrably influence queries/eval.

---

## Epic 11 — Hardening: evals, throttle tuning, dashboards, security · `phase-11` · *all lanes*

### 11.1 — Formal eval suite (CI-gated) · `lane:L2-agents`
**AC:** relevance/groundedness/recall on the golden dataset; runs in CI; scores tracked over time.
`labels: user-story, area:llm`.

### 11.2 — Load/throttle tuning · `lane:L1-orchestration`
**AC:** stays within TPM/RPM/QPS under load; backpressure verified; limits documented.
`labels: user-story, area:maf`.

### 11.3 — App Insights dashboards + alerts · `lane:L3-data-platform`
**AC:** dashboards for latency, tokens/cost, verdict mix, failures; alerts configured.
`labels: user-story, area:observability`.

### 11.4 — Security review · `lane:L3-data-platform`
**AC:** SSRF, secrets hygiene, least-privilege RBAC reviewed; findings tracked.
`labels: user-story, area:security`.

### 11.5 — Loop-feedback Option (B): structured `SearchDirective` steer · `lane:L2-agents` · *nice-to-have*
**AC:** upgrade the eval→query-synthesis steer from Phase 6's prose (story 6.5 / Option A) to a structured
`IReadOnlyList<SearchDirective>` on `ReviewDecision` — `Facet` + `Reason` enum
`MissingFacet|WeakEvidence|LowAuthority|Stale|Ambiguous` + optional `Note`. The query-synthesis prompt
gains matching interpretation instructions and the eval + query-synth prompts stay **versioned together**.
**Only pursue** if Phase 6 prose proves unreliable in practice or directive-type metrics are wanted.
`labels: user-story, area:llm` · **depends on:** 6.5.

### 11.6 — SSRF guard on fetch (full) · `lane:L3-data-platform` · *nice-to-have*
**AC:** harden the Fetch & clean step (story 5.2) beyond its basic scheme/size/redirect/content-type
caps: enforce a primary-source host allowlist, resolve and block private/loopback/link-local/metadata
IP ranges (incl. `169.254.169.254`) at connect time so redirects and DNS-rebinding are covered, and
re-validate each redirect hop. **Only pursue** if fetch ever targets non-curated/user-supplied URLs,
or as part of the 11.4 security review. `labels: user-story, area:security` · **depends on:** 5.2.

---

## Epic 12 — Fan-out & parallelization (MAF) · `phase-12` · *L1-led*
> Deferred on purpose: get the whole pipeline running **correctly and sequentially** first, then add
> concurrency. Replace the sequential run loop (1.1) with parallel per-topic-group execution under the
> shared throttle once Epics 1–11 are green.

### 12.1 — Run topic-group workflows in parallel under the shared throttle · `lane:L1-orchestration`
**AC:** replace the sequential loop (1.1) with `Task.WhenAll` gated by the shared throttle; active workers capped; per-group isolation preserved (one group failing does not abort the run); verified by test.
`labels: user-story, area:maf` · **depends on:** 1.1, 0.4 · after Epics 2–9 are green · *(parallel scope; was Epic 2)*

### 12.2 — In-flight concurrency traces & metrics · `lane:L3-data-platform`
**AC:** in-flight concurrency gauge + throttle wait-time metric emitted; parallel spans visible per run/group.
`labels: user-story, area:observability` · **depends on:** 12.1 · *(was Epic 2's concurrency metric)*

### 12.3 — Load/throttle tuning under parallel load · `lane:L1-orchestration`
**AC:** stays within TPM/RPM/QPS with N groups in flight; backpressure verified; per-group cap tuned and documented.
`labels: user-story, area:maf` · **depends on:** 12.1, 11.2.

### 12.4 — Decompose topic-group pass into per-step executors (mid-pass checkpointing + intra-group fan-out) · `lane:L1-orchestration` · `needs-design`
**As an** engineer, **I want** each pipeline step modeled as its own MAF executor wired by edges (with a conditional loop-back checked on the **Loop Controller** executor's response), **so that** the run checkpoints **between steps** (resume mid-pass instead of replaying a whole pass) and the Fetch & Clean / Relevance Eval steps can fan out per document.
**AC:**
- The **loop body** is decomposed into **seven executors** wired in the frozen order: **(1) Query Synthesis → (2) Web Search → (3) Pre-filter → (4) Fetch & Clean → (5) Relevance Eval → (6) Loop Controller → (7) Finalize**.
- The **branch is checked on the Loop Controller executor's response**: `Relevance Eval` emits the raw `ReviewDecision`; `LoopControllerExecutor` (today's `ILoopController.ReviewPassAsync`) applies the loop-control rules (per-group `maxLoops` cap, ≥80%-relevant override), maps verdicts to vetted/discarded items (persisting carried full text to blob), and emits the existing `Review` whose `FinalDecision` is a `LoopDecision` of `Retry` **or** `Finalize`. Two MAF conditional edges (`AddEdge(loopController, target, condition: …)`) route on `Review.FinalDecision`: **`Retry` loops back to Query Synthesis (executor #1)** for another pass, **`Finalize` exits the workflow** to the finalize tail. (We keep `LoopController` as a distinct node because the decision needs state a stateless edge predicate can't see — the pass count + cap — and has side effects; folding it into the eval step is rejected.)
- The **finalize chain** (Verdict Routing → Enrichment → Categorize → Summarize&Impact) stays a sequential tail (single `FinalizeExecutor` over today's `FinalizeAsync`), reached only on the `Finalize` edge — splitting it is out of scope here.
- `SearchHistory` + per-step payloads (query, hits, filtered hits, documents, eval decision) checkpointed so a mid-pass failure resumes **after the last completed step**; per-pass `SearchHistory` checkpointing owned by the Loop Controller executor.
- Fetch & Clean fan-out/fan-in across documents; per-step traces visible; existing pipeline tests pass (or are migrated).
- Design + trade-offs in **`docs/maf-executor-design.md`**.
`labels: user-story, area:maf, needs-design` · **depends on:** 12.1 · *pairs with the same decomposition that enables 12.1.*

**Epic demo:** the same pipeline that ran sequentially now runs topic groups **concurrently** under the throttle; throughput improves; the throttle caps active workers; parallel spans visible; cancellation still works.

---

## Epic 13 — FUTURE / post-POC (backlog) · `phase-13` · `future`
> Not scheduled for the POC; captured so they aren't lost (primer §5 deferrals).

- **13.1** Azure Function timer host (scheduled scans) · `lane:L1-orchestration` · `future`
- **13.2** Bicep IaC for all resources + Managed Identity role assignments · `lane:L3-data-platform` · `future`
- **13.3** Admin UI — review of past runs · `future`
- **13.4** Structured review capture (verdict correction + reason-code tags + freeform note) · `future`
- **13.5** Distillation job -> curated guidance rules into memory store · `future`
- **13.6** Async execution mode — background run + run-status store + `GET /runs/{runId}` polling + cancellation (merged out of the old Epic 2). Pull only if synchronous scans outgrow gateway timeouts (~230s on App Service) or a live-progress UI is needed (pairs with 13.3). · `lane:L1-orchestration` · `future`

---

## Sequencing cheat-sheet (what to pull first)

1. **Sprint-start (do as a group):** all of **Epic 0** — 0.1 -> 0.3 unblock everything. Pair on 0.3.
2. **Then L1** takes **Epic 1**, then the **Epic 2 workflow + controller stubs (2.1–2.3)**.
3. **In parallel L2** takes the **Epic 2 agent + tool stubs (2.4–2.9)**, then **Epic 3**.
4. **In parallel L3** takes **0.5/0.7**, then **Epic 5 storage (5.1)** and **Epic 8** prep.
5. **Sync points:** end of Epic 0 (contracts) and end of Epic 2 (agent I/O frozen).
6. **Keep execution sequential:** run topic groups **one at a time** through Epics 1–11; pull **Epic 12 (fan-out & parallelization)** only once the sequential pipeline is green end-to-end.

---

## Optional: create issues with the GitHub CLI

```powershell
# one example — repeat per story (or script from this file)
gh issue create `
  --title "0.3 Define core domain contracts (interface freeze)" `
  --body  "See docs/backlog.md -> Epic 0 -> 0.3. AC and dependencies listed there." `
  --label "user-story,lane:L2-agents,needs-design" `
  --milestone "phase-0"
```
