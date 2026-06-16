# Agentic RAG Scanner — Implementation Plan

> **Purpose.** A phased, vertically-sliced build plan for the `agentic-rag-scanner` project,
> derived from `architecture-context.md` (the architecture + context primer). It is designed so
> **3 engineers can work autonomously in parallel**, with a contract-first "interface freeze"
> early on and a *walking-skeleton-then-fill-in* strategy so the app is runnable and demoable at
> the end of every phase.
>
> **Source of truth:** `docs/architecture-context.md` (primer) **and**
> `docs/horizon-scanner-architecture.md` (the full design doc + Mermaid workflow diagram). This plan
> is reconciled against both; each phase maps to the numbered pipeline steps (shown as `? arch step N`).
>
> **How to use:** turn each **task** into a GitHub issue/PR. Each phase is a milestone. The
> "Parallel work" box in each phase shows how to split it across the 3 lanes.

---

## 0. Guiding principles

1. **Walking skeleton first.** Build the end-to-end pipe with *stub* agents/steps, then replace
   stubs with real functionality one vertical slice at a time. The app always runs.
2. **Contract-first (interface freeze).** Shared models + interfaces land in **Phase 0** so lanes
   can build against stable contracts without merge collisions.
3. **Vertical slices, demoable per phase.** Every phase ends with something you can run and show.
4. **PR-sized tasks.** Each task should be a few hours to ~2 days and reviewable in isolation.
5. **Deterministic vs. agentic separation.** LLM steps (agents) and deterministic steps
   (pre-filter, fetch, quality gates) are separate, independently testable units.
6. **POC scope honored.** FUTURE items from the primer (memory store, review/distillation, Function
   host, Admin UI) appear as later/optional phases, clearly marked.

---

## 1. Cross-cutting standards (apply in every phase)

These are the Azure + AI best practices everyone follows. Treat them as the "Definition of Done"
baseline for all code.

### Identity & secrets
- **Keyless by default:** `DefaultAzureCredential` (Azure.Identity) for Storage, Search, Cosmos,
  and Foundry. API keys/connection strings are **local-dev only**, via `appsettings.Local.json`
  (already git-ignored). No secrets in source/commits (public repo — primer §1).
- Plan for **Managed Identity** in deployed environments; later, secrets via Key Vault / App Config.

### Resilience & throttling
- **`Microsoft.Extensions.Http.Resilience`** (Polly v8 standard pipeline) on all outbound HTTP:
  retry-with-jitter, timeout, circuit breaker. Honor **`Retry-After`** on HTTP 429.
- **Shared throttle** across all parallel workflows (`SemaphoreSlim` and/or
  `System.Threading.RateLimiting`) to respect Azure OpenAI **TPM/RPM** and Bing **QPS** (arch §4).
  One injectable throttle service, not per-call.
- **Idempotency:** operations keyed by `runId` + item hash so retries never duplicate side effects.

### Configuration
- **Options pattern** with `ValidateDataAnnotations()` + `ValidateOnStart()` so misconfiguration
  fails fast at boot (extend the existing `*Options` classes).

### Observability
- **OpenTelemetry** (traces, metrics, logs) ? **Azure Monitor / Application Insights**
  (`Azure.Monitor.OpenTelemetry.AspNetCore`).
- **Correlation:** every log/span carries `runId` and `topicGroupId` via `ILogger` scopes.
- **AI telemetry:** record per-call **token usage**, latency, model/deployment, and per-run
  **verdict distribution** (RELEVANT/BORDERLINE/NOT_RELEVANT) as metrics.

### Governance & enterprise controls
- An **enterprise-controls layer** governs the sensitive nodes (fan-out, query synthesis, eval,
  summarize, quality gates) per the workflow diagram: **model routing**, **audit logging**,
  **observability**, and **access control**. Centralize these as cross-cutting middleware/policies
  rather than per-agent code.

### Cost controls
- Full-text eval of every surviving URL **× N topic groups** is the dominant cost driver (design-doc
  "risks"). Mitigate with the **allowlist + deterministic pre-filter** (fetch/eval fewer URLs),
  **cross-group dedupe** (never eval the same doc twice), token budgeting, and per-run **token/cost
  metrics** so spend is visible.

### AI-specific guardrails
- **Structured outputs:** agents return JSON validated against a schema (use response-format /
  JSON schema); never parse free text. Invalid JSON ? bounded retry ? fail the item, not the run.
- **Prompt management:** keep prompts **externalized and versioned**, not scattered inline. This
  repo's convention is a **`Prompts/` folder of C# prompt classes** (e.g. `QuerySynthesisPrompt.cs`)
  exposing typed methods that build the system prompt via string interpolation — **keep using that**.
  `.prompty` is an *optional* alternative file format (YAML front-matter + a templated prompt body)
  that some tooling can render/evaluate; it is **not required** and does **not** replace the C#
  prompt-class pattern. Pick one convention and apply it consistently — the C# prompt classes are the default here.
- **Determinism where it matters:** low temperature for eval/categorize; document the choice.
- **Token budgeting:** truncate/chunk fetched full-text before eval; keep within context limits.
- **Provenance:** every result item keeps its **source URL(s)**; carry citations through the loop.
- **Evaluation harness:** a small **golden dataset** + offline evals (relevance/groundedness).
  Compliance domain ? **false negatives are costlier than false positives** (primer §3); track recall.
- **Content safety:** screen inputs/outputs where appropriate (Azure AI Content Safety).

### Security
- **SSRF protection** on full-text fetch (step 7): enforce the **primary-source allowlist**, block
  private/loopback IPs, cap redirects, cap response size, allowlist content types.

### Testing
- **xUnit** + per-lane tests: unit tests for deterministic steps, **contract tests** for each
  service interface (against stubs), and **eval tests** for agents (gated, can run on a schedule).
- **Integration tests** use **Azurite** for Storage and the **shared dev Azure Cosmos account**
  (the same instance used for MAF checkpointing) — **no separate Cosmos emulator to manage**. Isolate
  each run with a disposable `it-{guid}` database/container that is torn down afterward to keep cost
  and cross-test interference low.

### CI/CD
- **GitHub Actions** (repo is on GitHub): `build` + `test` + `dotnet format` on every PR; PRs
  require green CI. Branch-per-task off `main`.

---

## 2. Team model — 3 lanes for 3 engineers

Phases are sliced so the three lanes are **mostly independent** between sync points. Lane owners
can be fixed for the project or rotate per phase.

| Lane | Focus | Owns (interfaces/areas) |
|------|-------|-------------------------|
| **L1 — Orchestration & Workflow** | Run lifecycle, fan-out, parallelism, shared throttle, MAF workflow + loop controller, verdict routing | `IScanOrchestrator`, MAF workflow/agent host, loop controller, throttle |
| **L2 — AI Agents & Grounding** | Foundry/LLM service, prompts, the 5 LLM agents, Bing grounding, fetch & clean, evals | `IFoundryService`, agent implementations, `IBingSearchGroundingService`, `IBingCustomSearchGroundingService`, fetch/clean |
| **L3 — Data, Quality & Platform** | Cosmos persistence, quality gates, blob storage, export/publish, Azure AI Search memory, observability, infra/CI | `IAzureStorageService`, `IAzureSearchService`, `ICosmosResultStore`, quality gates, export, OTel, Bicep |

> **Sync points** are called out per phase (the moments lanes must integrate). Outside those,
> lanes work against the Phase 0 contracts independently.

---

## 3. Recommended solution structure

To reduce merge conflicts with 3 engineers, introduce a **Core** project as the contract home.
Keep it light; do it once in Phase 0.

```
AgenticRagScanner.sln
?? AgenticRagScannerApi/            # host: controllers, DI, Program.cs (exists)
?? AgenticRagScanner.Core/          # NEW: domain models, enums, interfaces, SearchHistory, ResultItem
?? AgenticRagScanner.Workflows/     # NEW: MAF workflow + agents (L1/L2)  [added in Phase 3]
?? AgenticRagScanner.Tests/         # NEW: unit/contract/integration/eval tests
```

> The existing `Services/` and `Configuration/` may stay in the API host for now, or move to Core
> later — not a blocker. The key early move is **Core** so contracts are shared and stable.

---

## 4. The phases

Legend: `? arch step N` traces to `architecture-context.md` §3. **DoD** = Definition of Done.

---

### Phase 0 — Foundations & contracts (interface freeze) · *shared, do first*
**Goal:** establish shared contracts + cross-cutting scaffolding so lanes can diverge safely.
`? arch: cross-cutting`

**Tasks**
- [ ] **(L3)** Add `AgenticRagScanner.Core` project + reference it from the API.
- [ ] **(L3)** Add `AgenticRagScanner.Tests` (xUnit) + wire into CI.
- [ ] **(All)** Define core contracts in Core (the **interface freeze**):
  - `TopicGroup` (keyword/synonym OR-list + per-group `MaxLoops`, default 3), `RunContext`, `TopicGroupContext`
  - `SearchHistory` (in-memory, per group/run): `SearchQueries[]`, `VettedResults[]`,
    `DiscardedResults[]` (primer §3)
  - `ResultItem` schema: source URL(s), `WhatItDoes`, impact area, regulator, tags, `Verdict`
    (`RELEVANT|BORDERLINE|NOT_RELEVANT`), `PublicationDate`, `EffectiveDate`, `AppliesFrom`,
    `AppliesTo`, `DateConfidence`, level-of-authority, `Unverified`, `runId`, `groupId`, `version`
  - `Verdict`, `LevelOfAuthority` enums
- [ ] **(L1)** Add shared **throttle** abstraction (`ILlmThrottle`/`IRateLimiter`) — no real limits yet.
- [ ] **(L3)** Cross-cutting: OpenTelemetry skeleton (console exporter ok for now), options
  validation (`ValidateOnStart`), `/health` endpoint, `DefaultAzureCredential` registration.
- [ ] **(L3)** GitHub Actions CI (build + test + format).

**DoD / demo:** solution builds with Core + Tests; `/health` returns healthy; CI green; contracts
compile and are referenced by the API.

> **Sync point:** contracts reviewed & merged by all 3 before Phase 1. This is the most important
> gate in the plan.

---

### Phase 1 — Run lifecycle & topic-group fan-out · *L1-led*
**Goal:** accept the request, **fan out by topic group**, and run a placeholder per-group task.
`? arch steps 1–2`

**Tasks**
- [ ] **(L1)** `IScanOrchestrator` + implementation: map `ScanRequest` ? one `TopicGroupContext`
  per group (each seeded with an empty `SearchHistory`).
- [ ] **(L1)** Per-group **placeholder** pipeline (returns a stub result) so fan-out is observable.
- [ ] **(L1)** Run **status store** (in-memory): `runId` ? status + per-group progress.
- [ ] **(L1)** `GET /api/v1/scanner/runs/{runId}` status endpoint.
- [ ] **(L1)** Wire `ScannerController.Scan` to start the run and return `202` + `runId` (replace TODO).
- [ ] **(L3)** Structured logging scopes (`runId`, `topicGroupId`) around fan-out.

**DoD / demo:** `POST scan` with 3 topic groups ? `202` + `runId`; `GET runs/{runId}` shows 3 groups
moving to "completed (stub)". No MAF yet.

---

### Phase 2 — Parallel execution harness + shared throttle · *L1-led*
**Goal:** run the N group pipelines **truly in parallel** under the shared throttle; make scans
long-running/background. `? arch steps 2–3 (concurrency, arch §4)`

**Tasks**
- [ ] **(L1)** Execute groups with `Task.WhenAll` gated by the shared throttle (cap concurrency).
- [ ] **(L1)** Background execution (e.g. hosted background queue/`Channel`) so the HTTP call
  returns immediately and the run continues; status endpoint reflects progress.
- [ ] **(L1)** Cancellation: cancel a run via token; partial results preserved.
- [ ] **(L3)** Traces/metrics: span per run, per group; metric for in-flight concurrency.

**DoD / demo:** 5 groups run concurrently but throttle limits active workers; status shows live
progress; a run can be cancelled; traces show parallel spans.

> **Sync point:** end of Phase 2 the **skeleton is ready**. L2 and L3 can now build agents/data
> against the contracts largely independently.

---

### Phase 3 — MAF workflow scaffolding (stub agents) · *L1 + L2*
**Goal:** introduce **Microsoft Agent Framework** and build the **agentic RAG loop** per group with
**all agents + steps present but stubbed** (canned, schema-valid outputs). `? arch step 3 (loop), 4–14 as stubs`

#### Agents vs. deterministic steps (important)
You're right that the loop is built from **MAF agents** — but **not every node is an LLM agent**.
The design-doc diagram marks five **"Agent"** nodes (LLM-backed); the rest are **deterministic
tools/steps** the workflow calls between agents. Getting this split right keeps cost down and makes
each unit independently testable.

- **LLM agents (5):** Query Synthesis (4), Relevance Eval (9), Enrichment (12), Categorize (13),
  Summarize & Impact (14).
- **Deterministic steps (not agents):** Bing Search (5) — a **tool/connector**, not an LLM call;
  Pre-filter (6); Fetch & Clean (7); Loop Controller (10); Verdict Routing (11). *(So "an agent does
  the Bing search" is the one to adjust — Bing is a tool the workflow invokes, gated to the allowlist.)*

#### The agent roster (build each as a stub in this phase, fill in later)

| # | Agent (LLM) | Role in the workflow | Input ? Output (contract) | Real impl. phase |
|---|-------------|----------------------|---------------------------|------------------|
| 4 | **Query Synthesis Agent** | Turn the topic-group keyword/synonym OR-list into focused search quer(ies); on re-loops read `SearchHistory` to rotate synonyms / fill gaps. Decides *how many* queries. | `TopicGroupContext` + `SearchHistory` ? `string[] queries` | **4** |
| 9 | **Relevance Eval Agent** | Single full-text call ? `RELEVANT/BORDERLINE/NOT_RELEVANT`; effective-date aware; applies retrieved learnings; judges goal coverage. | cleaned full text + dates + `SearchHistory` ? `Verdict` + date fields + rationale | **7** |
| 12 | **Enrichment Agent** | Post-verdict enrichment only (relevance already decided): `whatItDoes` summary + metadata. | carried `ResultItem` ? enriched `ResultItem` | **8** |
| 13 | **Categorize Agent** | Assign impact area, regulator, and **approved tags only** (controlled vocabulary). | enriched `ResultItem` ? category fields | **8** |
| 14 | **Summarize & Impact Agent** | RAG over in-memory history ? plain-English impact summary + effective-date framing. | enriched `ResultItem` + `SearchHistory` ? summary/impact | **8** |

> The two controller nodes — **Loop Controller (10)** and **Verdict Routing (11)** — are deterministic
> orchestration owned by **L1**, not agents. Bing Search (5) is registered as a **tool/connector** so
> a later option is to let the Query Synthesis agent call it as a function; for the POC the workflow
> invokes it deterministically right after synthesis.

**Tasks — workflow & orchestration (L1)**
- [ ] Add `AgenticRagScanner.Workflows`; define **one MAF workflow per topic group**, with MAF
  **Cosmos checkpointing** wired to the shared Azure Cosmos account (see Phase 9) so long runs are durable/resumable.
- [ ] Build the loop scaffold threading `SearchHistory` through each pass:
  `QuerySynthesis ? BingSearch(tool) ? Pre-filter ? Fetch&Clean ? RelevanceEval ? LoopController ?`
  `VerdictRouting ? Enrichment ? Categorize ? Summarize&Impact`.
- [ ] **Loop Controller** stub (deterministic): honor per-group `maxLoops` (default 3); append each pass to `SearchHistory`.
- [ ] **Verdict Routing** stub (deterministic): RELEVANT/BORDERLINE ? enrichment; NOT_RELEVANT ? dropped + logged.
- [ ] Register **Bing Search as a tool/connector** (allowlist-gated) — stubbed to return canned hits.

**Tasks — agent stubs (L2)** *(one PR per agent — naturally parallel across the team)*
- [ ] **Query Synthesis Agent** stub — returns 1–2 canned queries from the keyword set.
- [ ] **Relevance Eval Agent** stub — returns a canned `Verdict` + date fields per item.
- [ ] **Enrichment Agent** stub — returns a canned `whatItDoes` + metadata.
- [ ] **Categorize Agent** stub — returns canned impact area / regulator / approved tags.
- [ ] **Summarize & Impact Agent** stub — returns a canned plain-English summary.
- [ ] For each stub: define its MAF agent definition (name, instructions placeholder, I/O type),
  register it in DI, and add a `Prompts/<Agent>Prompt.cs` placeholder (see prompt-management standard).

**DoD / demo:** a run executes the **entire loop end-to-end with fake data**, loops up to `maxLoops`,
routes verdicts, emits stub `ResultItem`s, and **checkpoints to Cosmos**. No external LLM/Bing calls yet.

> **Sync point:** the **agent I/O contracts** (table above) are frozen here. From this point each
> downstream phase swaps **one stub for a real implementation** — so the 3 engineers can each own
> different agents in parallel (e.g. L2a: Query Synthesis+Eval; L2b: Enrichment/Categorize/Summarize)
> without colliding.

---

### Phase 4 — Foundry LLM service + Query Synthesis Agent (first real agent) · *L2-led*
**Goal:** make LLM calls real; implement the first agent. `? arch step 4`

**Tasks**
- [ ] **(L2)** Implement `IFoundryService` against Microsoft Foundry using `DefaultAzureCredential`
  (prefer an `IChatClient` abstraction via `Microsoft.Extensions.AI`); add resilience + throttle.
- [ ] **(L2)** Prompt management: externalized, versioned prompt templates.
- [ ] **(L2)** **Query Synthesis Agent** (real): synthesize focused queries from the keyword set;
  on re-loops consult `SearchHistory` to **rotate synonym coverage** and avoid redundancy (primer §2/§3).
- [ ] **(L2)** Structured output + validation; bounded retry on invalid JSON.
- [ ] **(L3)** Token-usage + latency metrics for the agent.

**DoD / demo:** real, non-redundant queries generated from a topic group; second loop targets
untested synonyms/gaps.

---

### Phase 5 — Bing grounding search + deterministic pre-filter · *L2 + L1/L3*
**Goal:** real grounded search restricted to the allowlist, then deterministic pre-filter.
`? arch steps 5–6`

**Tasks**
- [ ] **(L2)** Implement `IBingSearchGroundingService` (Grounding with Bing Search) **gated to the
  primary-source allowlist at query time** (primer §2).
- [ ] **(L2)** Implement `IBingCustomSearchGroundingService` (custom-scoped config) — parity.
- [ ] **(L1/L3)** **Deterministic pre-filter:** dedupe (incl. **cross-group**), URL
  reachability/validity. Pure functions, fully unit-tested.

**DoD / demo:** loop fetches real results restricted to allowlisted domains; duplicates (including
across groups) removed; dead URLs dropped.

---

### Phase 6 — Full-text fetch & clean + blob storage · *L3 + L2*
**Goal:** fetch & clean source content; persist artifacts. `? arch step 7`

**Tasks**
- [ ] **(L3)** Implement `IAzureStorageService.UploadBlobAsync` (BlobServiceClient +
  `DefaultAzureCredential`); containers from options.
- [ ] **(L2)** Fetch HTML/PDF, **strip boilerplate**; on failure **fall back to Bing summary +
  flag `Unverified`** (do not drop — primer §3).
- [ ] **(L3)** **SSRF guard:** allowlist enforcement, block private IPs, cap size/redirects/content-types.
- [ ] **(L3)** Store cleaned text/artifacts to blob; reference URI on the `ResultItem`.

**DoD / demo:** cleaned full-text stored in blob; unreachable docs produce an `Unverified` item via
summary fallback; SSRF guard rejects non-allowlisted hosts.

---

### Phase 7 — Relevance eval (3-verdict, date-aware) + real loop controller · *L2 + L1*
**Goal:** the core compliance decision + loop exit logic. `? arch steps 9–11`

**Tasks**
- [ ] **(L2)** **Relevance Eval Agent** (real): single LLM call over **full text**;
  `RELEVANT|BORDERLINE|NOT_RELEVANT`; **effective-date aware** (publication vs effective/in-force vs
  tax-year applicability) ? fills `PublicationDate`/`EffectiveDate`/`AppliesFrom`/`AppliesTo`/
  `DateConfidence`; dates as a **signal, not a hard filter** (primer §3).
- [ ] **(L1)** **Loop controller** (real): re-loop if under `maxLoops` **and** goal unmet, **or
  override** if a pass returns **>80% RELEVANT**; update `SearchHistory` each pass. `maxLoops` is
  **tunable per topic group** — larger synonym-heavy groups (e.g. Miscellaneous / IR35) may use a
  higher cap so synthesis can rotate coverage across more passes; small groups stay at 3 or lower.
- [ ] **(L1)** **Verdict routing** (real): BORDERLINE carried forward but flagged; NOT_RELEVANT
  dropped + logged for audit.
- [ ] **(L3)** Verdict-distribution metric; eval-harness recall check on golden set.

**DoD / demo:** items classified with verdicts + dates; loop exits per the rules; BORDERLINE items
flagged and carried; NOT_RELEVANT logged.

---

### Phase 8 — Enrichment + Categorize + Summarize/Impact agents · *L2-led*
**Goal:** finish the downstream agents. `? arch steps 12–14`

**Tasks**
- [ ] **(L2)** **Content Analysis / Enrichment** (real): `WhatItDoes` summary; enrich metadata.
- [ ] **(L2)** **Categorize Agent** (Stage 2): impact area, regulator, **approved tags only**.
- [ ] **(L2)** **Summarize & Impact Agent** (Stage 3): RAG over in-memory history; effective date;
  **plain-English** impact.

**DoD / demo:** each carried item is enriched, categorized with approved tags, and has a
plain-English impact summary.

---

### Phase 9 — Deterministic quality gates + Cosmos persistence · *L3-led*
**Goal:** validate, dedupe vs store, stamp authority, persist. `? arch steps 15–16`

**Tasks**
- [ ] **(L3)** **Quality gates** (non-LLM): JSON-schema validation; dedupe **vs Cosmos**;
  **level-of-authority stamping** (legislation > court ruling > HMRC guidance).
- [ ] **(L3)** `ICosmosResultStore` (Microsoft.Azure.Cosmos + `DefaultAzureCredential`):
  **one versioned doc per item per run**; partition-key strategy (e.g. by jurisdiction or `runId`);
  **idempotent** upsert (ETag/optimistic concurrency).
- [ ] **(L3)** Reuse the **same Azure Cosmos account as the MAF checkpoint store** (see Phase 3) —
  separate containers for `checkpoints` vs `results`; one account, no emulator.

**DoD / demo:** results persisted in Cosmos as versioned docs; re-running a run does not duplicate;
authority level stamped.

---

### Phase 10 — Publish & export · *L3-led*
**Goal:** publish results and export. `? arch step 17`

**Tasks**
- [ ] **(L3)** Auto-publish published-update view from Cosmos.
- [ ] **(L3)** **CSV/Excel export** to blob; expose a download/link endpoint.

**DoD / demo:** completed run produces a downloadable CSV/Excel of the published updates.

---

### Phase 11 — Memory / learnings store (Azure AI Search) · *L3 + L2* · *was FUTURE #8*
**Goal:** cross-run learnings feeding synthesis + eval. `? arch step 8 (planned)`

**Tasks**
- [ ] **(L3)** Implement `IAzureSearchService` (Azure.Search.Documents + `DefaultAzureCredential`):
  index for curated learnings; vector/hybrid retrieval.
- [ ] **(L2)** Feed retrieved learnings into Query Synthesis + Relevance Eval (RAG); distinct from
  the per-run in-memory `SearchHistory`.

**DoD / demo:** prior-run learnings are retrieved and demonstrably influence queries/eval.

---

### Phase 12 — Hardening: evals, throttle tuning, dashboards, security · *all lanes*
**Goal:** make it production-credible.

**Tasks**
- [ ] **(L2)** Formal **eval suite** (relevance/groundedness/recall) on the golden dataset; CI-gated.
- [ ] **(L1)** Load/throttle tuning to stay within TPM/RPM/QPS; backpressure verified.
- [ ] **(L3)** App Insights **dashboards** (latency, tokens, verdict mix, failures); alerts.
- [ ] **(All)** Security review (SSRF, secrets hygiene, least-privilege RBAC roles).

**DoD / demo:** eval scores tracked over time; dashboards live; documented limits respected.

---

### Phase 13 — FUTURE / post-POC (not scheduled) · *backlog*
`? arch steps 18–20 + primer §5 deferrals`
- [ ] Azure **Function** timer host (scheduled scans) alongside the Web API.
- [ ] **Bicep** infra-as-code for all resources + Managed Identity role assignments.
- [ ] **Admin UI** — review of past runs.
- [ ] **Structured review capture** (verdict correction + reason codes + notes).
- [ ] **Distillation job** rolling reviews into curated guidance ? memory store (Phase 11).

---

## 5. Dependency & parallelization map

```
Phase 0 (contracts) ????> Phase 1 ?> Phase 2 ???> Phase 3 (skeleton) ??> from here, parallel:
                      ?                        ?
                      ?                        ?? L2 track: Phase 4 ?> 5 ?> 6 ?> 7 ?> 8
                      ?                        ?
                      ??????????????????????????? L3 track: Phase 6 (storage) ? Phase 9 ?> 10 ?> 11
                                               ?
                                               ?? L1 track: Phase 7 loop controller ? Phase 12 tuning
```

- **Serial spine (everyone depends on):** Phase 0 ? 1 ? 2 ? 3.
- **After Phase 3**, the three lanes proceed largely in parallel:
  - **L2** drives the agent chain (4 ? 5 ? 6 fetch ? 7 ? 8).
  - **L3** drives storage (6) + persistence/export/memory (9 ? 10 ? 11) — can start storage early.
  - **L1** owns loop controller/verdict routing (7) and concurrency/throttle tuning (2, 12).
- **Integration sync points:** end of Phase 0 (contracts), end of Phase 3 (loop shape), and before
  Phase 9 (ResultItem schema final).

---

## 6. Global Definition of Done (every PR)

- [ ] Builds; `dotnet format` clean; CI green.
- [ ] Unit/contract tests for new logic; deterministic steps covered.
- [ ] No secrets/customer-identifying material (primer §1); placeholders only.
- [ ] Uses `DefaultAzureCredential` (no keys in non-local paths).
- [ ] Outbound calls use the resilience pipeline + shared throttle.
- [ ] Logs carry `runId`/`topicGroupId`; new AI calls emit token/latency telemetry.
- [ ] Traceable to an `architecture-context.md` step; doc updated if behavior changed.

---

## 7. Suggested first branch

Start with **Phase 0**, because it's the contract freeze the other two engineers build on:

```
feat/phase-0-foundations-and-contracts
```

Split Phase 0 across the team immediately:
- **L3:** Core + Tests projects, CI, OTel/health skeleton.
- **L1:** throttle abstraction + run/group context shapes.
- **L2:** `SearchHistory` + `ResultItem` + verdict/date enums (the agent-facing contracts).

Once Phase 0 merges, branch **Phase 1** (`feat/phase-1-topic-group-fanout`, L1-led) while L2/L3
begin prepping their tracks against the frozen contracts.
