# Agentic RAG Scanner — Sprint Backlog (Epics & User Stories)

> Companion to `docs/implementation-plan.md`. Each **Epic = a phase**; each **story** is a
> board-ready issue with acceptance criteria, a suggested lane owner, labels, and dependencies.
> Transcribe directly onto the GitHub Project board, or create with `gh issue create` (see bottom).
>
> **Lanes:** **L1** Orchestration & Workflow · **L2** AI Agents & Grounding · **L3** Data, Quality & Platform.
> **Global DoD** (`implementation-plan.md §6`) applies to *every* story on top of its own criteria.
>
> **Note on IDs:** some story numbers are intentionally skipped where small stories were merged
> (e.g. `1.4`, `2.3`, `3.4`). IDs are **stable references** for the board, not a contiguous sequence;
> merged stories are annotated *(merged: former X + Y)*.

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

## Epic 0 — Foundations & contracts (interface freeze) · `phase-0`
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
  `Unverified`, `RunId`, `GroupId`, `Version`.
- `Verdict` and `LevelOfAuthority` enums.
- Reviewed & approved by all 3 engineers before merge.
`labels: user-story, needs-design` · **depends on:** 0.1 · **blocks:** Phases 1–11.

### 0.4 — Shared throttle abstraction · `lane:L1-orchestration`
**AC:** `ILlmThrottle`/rate-limiter interface in Core (no real limits yet); unit-testable; DI-registered.
`labels: user-story, area:maf` · **depends on:** 0.1.

### 0.5 — Cross-cutting skeleton: OTel + options validation + health + identity · `lane:L3-data-platform`
**AC:** OpenTelemetry wired (console exporter ok); `*Options` use `ValidateDataAnnotations()` +
`ValidateOnStart()`; `/health` endpoint healthy; `DefaultAzureCredential` registered.
`labels: user-story, area:observability` · **depends on:** 0.1.

### 0.7 — Chore: genericize customer material in design doc · `lane:L3-data-platform`
**AC:** "Reg Advantage" ? generic term; `Topic Groups.docx` reference genericized; no customer-identifying
text remains (primer §1).
`labels: chore, area:docs, good-first-issue`.

---

## Epic 1 — Run lifecycle & topic-group fan-out · `phase-1` · *L1-led*

### 1.1 — Scan orchestrator: fan-out + controller trigger · `lane:L1-orchestration`
**As an** auditor, **I want** a scan to start from the API, **so that** the request fans out into per-group work.
**AC:**
- `IScanOrchestrator` maps `ScanRequest` ? one `TopicGroupContext` per group, each seeded with an empty `SearchHistory`.
- `ScannerController.Scan` calls the orchestrator (replaces the current TODO), returns `202` + `runId`, and logs the accepted run.
`labels: user-story, area:maf` · **depends on:** 0.3, 0.4 · *(merged: former 1.1 + 1.5)*

### 1.2 — Per-group placeholder pipeline · `lane:L1-orchestration`
**AC:** each group runs a stub step returning a placeholder result; fan-out observable in logs.
`labels: user-story` · **depends on:** 1.1.

### 1.3 — Run status store + `GET /runs/{runId}` endpoint · `lane:L1-orchestration`
**AC:**
- In-memory store: `runId` ? status + per-group progress, queryable in-process.
- `GET /runs/{runId}` returns run status + per-group progress; `404` for an unknown run.
`labels: user-story` · **depends on:** 1.1 · *(merged: former 1.3 + 1.4)*

**Epic demo:** `POST scan` (3 groups) ? `202` + `runId`; `GET runs/{runId}` shows 3 groups completing (stub).

---

## Epic 2 — Parallel execution harness + shared throttle · `phase-2` · *L1-led*

### 2.1 — Run groups in parallel under the shared throttle · `lane:L1-orchestration`
**AC:** `Task.WhenAll` gated by throttle; active workers capped; verified by test.
`labels: user-story, area:maf` · **depends on:** 1.2, 0.4.

### 2.2 — Background execution + cancellation · `lane:L1-orchestration`
**AC:**
- HTTP trigger returns immediately; the run continues on a hosted background worker (`Channel`/queue); status reflects progress.
- A run can be cancelled via token; partial results preserved; status shows "cancelled".
`labels: user-story` · **depends on:** 2.1 · *(merged: former 2.2 + 2.3)*

### 2.3 — Concurrency traces & metrics · `lane:L3-data-platform`
**AC:** span per run + per group; in-flight concurrency metric emitted.
`labels: user-story, area:observability` · **depends on:** 2.1.

**Epic demo:** 5 groups run concurrently but throttle limits workers; live progress; cancellable; parallel spans visible.

---

## Epic 3 — MAF workflow scaffolding (stub agents) · `phase-3` · *L1 + L2*
> All agents + steps present but **stubbed**. **Sync point:** agent I/O contracts frozen here.

### 3.1 — `AgenticRagScanner.Workflows` + one MAF workflow per group + Cosmos checkpointing · `lane:L1-orchestration`
**AC:** Workflows project added; one MAF workflow per topic group; **MAF Cosmos checkpointing** wired to
the shared dev Cosmos account (`checkpoints` container); a run is resumable from checkpoint.
`labels: user-story, area:maf, area:cosmos` · **depends on:** 2.2 · `needs-design` (confirm MAF checkpoint API).

### 3.2 — Loop scaffold threading `SearchHistory` · `lane:L1-orchestration`
**AC:** ordered loop wired: QuerySynthesis ? BingSearch(tool) ? Pre-filter ? Fetch&Clean ? RelevanceEval ?
LoopController ? VerdictRouting ? Enrichment ? Categorize ? Summarize&Impact; `SearchHistory` passed each pass.
`labels: user-story, area:maf` · **depends on:** 3.1.

### 3.3 — Loop Controller + Verdict Routing stubs (deterministic) · `lane:L1-orchestration`
**AC:**
- Loop Controller stub honors per-group `maxLoops` (default 3); appends each pass to `SearchHistory`.
- Verdict Routing stub: RELEVANT/BORDERLINE ? enrichment; NOT_RELEVANT ? dropped + logged for audit.
`labels: user-story` · **depends on:** 3.2 · *(merged: former 3.3 + 3.4)*

### 3.5 — Register Bing Search as an allowlist-gated tool (stub) · `lane:L2-agents`
**AC:** Bing registered as a **tool/connector** (not an LLM agent), returns canned hits; allowlist hook present.
`labels: user-story, area:bing` · **depends on:** 3.2.

### 3.6 — Stub: Query Synthesis Agent · `lane:L2-agents`
**AC:** MAF agent definition + DI + `Prompts/QuerySynthesisPrompt.cs` placeholder; returns 1–2 canned queries.
`labels: user-story, area:llm` · **depends on:** 0.3, 3.2.

### 3.7 — Stub: Relevance Eval Agent · `lane:L2-agents`
**AC:** returns canned `Verdict` + date fields; agent def + DI + prompt placeholder.
`labels: user-story, area:llm` · **depends on:** 0.3, 3.2.

### 3.8 — Stub: Enrichment Agent · `lane:L2-agents`
**AC:** returns canned `whatItDoes` + metadata; agent def + DI + prompt placeholder.
`labels: user-story, area:llm` · **depends on:** 0.3, 3.2.

### 3.9 — Stub: Categorize Agent · `lane:L2-agents`
**AC:** returns canned impact area / regulator / approved tags; agent def + DI + prompt placeholder.
`labels: user-story, area:llm` · **depends on:** 0.3, 3.2.

### 3.10 — Stub: Summarize & Impact Agent · `lane:L2-agents`
**AC:** returns canned plain-English summary; agent def + DI + prompt placeholder.
`labels: user-story, area:llm` · **depends on:** 0.3, 3.2.

**Epic demo:** full loop runs end-to-end on fake data, loops to `maxLoops`, routes verdicts, emits stub
`ResultItem`s, **checkpoints to Cosmos**. No external LLM/Bing calls.

---

## Epic 4 — Foundry LLM service + Query Synthesis (first real agent) · `phase-4` · *L2-led*

### 4.1 — Implement `IFoundryService` (real LLM calls) · `lane:L2-agents`
**AC:** calls Microsoft Foundry via `DefaultAzureCredential` (prefer `IChatClient` /
`Microsoft.Extensions.AI`); resilience pipeline + shared throttle applied; token/latency metrics.
`labels: user-story, area:llm` · **depends on:** 3.7 (or 3.6).

### 4.2 — Prompt management convention (`Prompts/*.cs`) · `lane:L2-agents`
**AC:** documented pattern; `QuerySynthesisPrompt.cs` builds the system prompt via interpolation; versioned.
`labels: user-story, area:llm` · **depends on:** 4.1.

### 4.3 — Query Synthesis Agent (real) · `lane:L2-agents`
**AC:** synthesizes focused queries from the keyword set; on re-loop reads `SearchHistory` to rotate
synonyms / avoid redundancy; agent decides query count; structured output + bounded retry on invalid JSON.
`labels: user-story, area:llm` · **depends on:** 4.1, 4.2.

**Epic demo:** real non-redundant queries; second loop targets untested synonyms/gaps.

---

## Epic 5 — Bing grounding + deterministic pre-filter · `phase-5` · *L2 + L1/L3*

### 5.1 — Bing grounding services (Search + Custom Search), allowlist-gated · `lane:L2-agents`
**AC:**
- `IBingSearchGroundingService`: Grounding with Bing Search restricted to the primary-source allowlist **at query time**.
- `IBingCustomSearchGroundingService`: custom-scoped config implemented to parity.
`labels: user-story, area:bing` · **depends on:** 3.5 · *(merged: former 5.1 + 5.2)*

### 5.3 — Deterministic pre-filter (dedupe incl. cross-group + URL validity) · `lane:L3-data-platform`
**AC:** pure, unit-tested functions; cross-group dedupe; unreachable/invalid URLs dropped.
`labels: user-story` · **depends on:** 0.3.

**Epic demo:** real allowlisted results; duplicates (incl. cross-group) removed; dead URLs dropped.

---

## Epic 6 — Full-text fetch & clean + blob storage · `phase-6` · *L3 + L2*

### 6.1 — `IAzureStorageService.UploadBlobAsync` (real) · `lane:L3-data-platform`
**AC:** BlobServiceClient + `DefaultAzureCredential`; containers from options; returns blob URI.
`labels: user-story, area:storage` · **depends on:** 0.5.

### 6.2 — Fetch & clean HTML/PDF with summary fallback · `lane:L2-agents`
**AC:** fetch HTML/PDF, strip boilerplate; on failure fall back to Bing summary + flag `Unverified` (never drop).
`labels: user-story` · **depends on:** 5.1.

### 6.3 — SSRF guard on fetch · `lane:L3-data-platform`
**AC:** allowlist enforced; private/loopback IPs blocked; caps on size/redirects/content-types; tested.
`labels: user-story, area:security` · **depends on:** 6.2.

### 6.4 — Persist cleaned artifacts to blob · `lane:L3-data-platform`
**AC:** cleaned text/artifacts stored; blob URI referenced on the `ResultItem`.
`labels: user-story, area:storage` · **depends on:** 6.1, 6.2.

**Epic demo:** cleaned full-text in blob; unreachable docs ? `Unverified` via fallback; SSRF guard rejects non-allowlisted hosts.

---

## Epic 7 — Relevance eval (3-verdict, date-aware) + real loop controller · `phase-7` · *L2 + L1*

### 7.1 — Relevance Eval Agent (real, full-text, date-aware) · `lane:L2-agents`
**AC:** single full-text call ? `RELEVANT/BORDERLINE/NOT_RELEVANT`; distinguishes publication vs
effective vs tax-year dates ? fills date fields + `DateConfidence`; dates as signal, not hard filter.
`labels: user-story, area:llm` · **depends on:** 4.1, 6.2.

### 7.2 — Loop Controller + Verdict Routing (real) · `lane:L1-orchestration`
**AC:**
- Loop Controller: re-loop if under `maxLoops` AND goal unmet, OR override if a pass is >80% RELEVANT; `maxLoops` tunable per topic group; `SearchHistory` updated each pass.
- Verdict Routing: BORDERLINE carried forward but flagged in the data structure; NOT_RELEVANT dropped + logged.
`labels: user-story, area:maf` · **depends on:** 7.1, 3.3 · *(merged: former 7.2 + 7.3)*

### 7.4 — Verdict-distribution metric + recall check on golden set · `lane:L3-data-platform`
**AC:** verdict mix emitted as a metric; eval-harness recall check runs on the golden dataset.
`labels: user-story, area:observability` · **depends on:** 7.1.

**Epic demo:** items classified with verdicts + dates; loop exits per rules; BORDERLINE flagged/carried; NOT_RELEVANT logged.

---

## Epic 8 — Enrichment + Categorize + Summarize/Impact (real) · `phase-8` · *L2-led*

### 8.1 — Enrichment Agent (real) · `lane:L2-agents`
**AC:** `whatItDoes` summary + enriched metadata on carried items.
`labels: user-story, area:llm` · **depends on:** 4.1, 7.2.

### 8.2 — Categorize Agent (real, approved tags only) · `lane:L2-agents`
**AC:** impact area + regulator + tags from the controlled vocabulary only.
`labels: user-story, area:llm` · **depends on:** 8.1.

### 8.3 — Summarize & Impact Agent (real, plain-English) · `lane:L2-agents`
**AC:** RAG over in-memory history; effective-date framing; plain-English impact summary.
`labels: user-story, area:llm` · **depends on:** 8.1.

**Epic demo:** each carried item enriched, categorized with approved tags, with a plain-English impact summary.

---

## Epic 9 — Quality gates + Cosmos persistence · `phase-9` · *L3-led*

### 9.1 — Deterministic quality gates · `lane:L3-data-platform`
**AC:** JSON-schema validation; dedupe vs Cosmos; level-of-authority stamping
(legislation > court ruling > HMRC guidance); bad/dup records rejected.
`labels: user-story, area:cosmos` · **depends on:** 0.3.

### 9.2 — `ICosmosResultStore` (versioned docs, idempotent) · `lane:L3-data-platform`
**AC:** Microsoft.Azure.Cosmos + `DefaultAzureCredential`; one versioned doc per item per run;
partition-key strategy; idempotent upsert (ETag); **same account as MAF checkpoints**, separate `results` container.
`labels: user-story, area:cosmos` · **depends on:** 9.1, 3.1.

**Epic demo:** results persisted as versioned Cosmos docs; re-run does not duplicate; authority stamped.

---

## Epic 10 — Publish & export · `phase-10` · *L3-led*

### 10.1 — Publish view + CSV/Excel export · `lane:L3-data-platform`
**AC:**
- Completed run auto-produces the published-update view (generic publishing target — no customer brand).
- CSV/Excel export generated to blob; endpoint returns a download/link.
`labels: user-story, area:storage` · **depends on:** 9.2, 6.1 · *(merged: former 10.1 + 10.2)*

**Epic demo:** completed run yields a downloadable CSV/Excel of published updates.

---

## Epic 11 — Memory / learnings store (Azure AI Search) · `phase-11` · *L3 + L2* · `future`-leaning

### 11.1 — `IAzureSearchService` for curated learnings · `lane:L3-data-platform`
**AC:** Azure.Search.Documents + `DefaultAzureCredential`; index for learnings; vector/hybrid retrieval.
`labels: user-story, area:search` · **depends on:** 0.5.

### 11.2 — Feed retrieved learnings into synthesis + eval · `lane:L2-agents`
**AC:** learnings retrieved (scoped to jurisdiction + topic group, top-K + recency) and injected into
Query Synthesis + Relevance Eval; distinct from per-run `SearchHistory`.
`labels: user-story, area:llm` · **depends on:** 11.1, 4.3, 7.1.

**Epic demo:** prior-run learnings retrieved and demonstrably influence queries/eval.

---

## Epic 12 — Hardening: evals, throttle tuning, dashboards, security · `phase-12` · *all lanes*

### 12.1 — Formal eval suite (CI-gated) · `lane:L2-agents`
**AC:** relevance/groundedness/recall on the golden dataset; runs in CI; scores tracked over time.
`labels: user-story, area:llm`.

### 12.2 — Load/throttle tuning · `lane:L1-orchestration`
**AC:** stays within TPM/RPM/QPS under load; backpressure verified; limits documented.
`labels: user-story, area:maf`.

### 12.3 — App Insights dashboards + alerts · `lane:L3-data-platform`
**AC:** dashboards for latency, tokens/cost, verdict mix, failures; alerts configured.
`labels: user-story, area:observability`.

### 12.4 — Security review · `lane:L3-data-platform`
**AC:** SSRF, secrets hygiene, least-privilege RBAC reviewed; findings tracked.
`labels: user-story, area:security`.

---

## Epic 13 — FUTURE / post-POC (backlog) · `phase-13` · `future`
> Not scheduled for the POC; captured so they aren't lost (primer §5 deferrals).

- **13.1** Azure Function timer host (scheduled scans) · `lane:L1-orchestration` · `future`
- **13.2** Bicep IaC for all resources + Managed Identity role assignments · `lane:L3-data-platform` · `future`
- **13.3** Admin UI — review of past runs · `future`
- **13.4** Structured review capture (verdict correction + reason-code tags + freeform note) · `future`
- **13.5** Distillation job ? curated guidance rules into memory store · `future`

---

## Sequencing cheat-sheet (what to pull first)

1. **Sprint-start (do as a group):** all of **Epic 0** — 0.1 ? 0.3 unblock everything. Pair on 0.3.
2. **Then L1** takes **Epic 1 ? 2 ? 3 (workflow + controller stubs 3.1–3.3, 3.5)**.
3. **In parallel L2** takes **Epic 3 agent stubs (3.6–3.10)** ? **Epic 4**.
4. **In parallel L3** takes **0.5/0.6**, then **Epic 6 storage (6.1)** and **Epic 9** prep.
5. **Sync points:** end of Epic 0 (contracts) and end of Epic 3 (agent I/O frozen).

---

## Optional: create issues with the GitHub CLI

```powershell
# one example — repeat per story (or script from this file)
gh issue create `
  --title "0.3 Define core domain contracts (interface freeze)" `
  --body  "See docs/backlog.md ? Epic 0 ? 0.3. AC and dependencies listed there." `
  --label "user-story,lane:L2-agents,needs-design" `
  --milestone "phase-0"
```
