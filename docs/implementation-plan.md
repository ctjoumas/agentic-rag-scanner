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
| **L2 — AI Agents & Grounding** | Foundry model deployment/LLM service, prompts, the five MAF agents (incl. Query Synthesis, over a Foundry deployment) + the **Web Search Foundry agent** (Grounding with Bing Custom Search tool), fetch & clean, evals | `IFoundryService` (Foundry project + model deployment), the **Web Search Foundry agent** (Bing Custom Search grounding tool), MAF agent implementations, fetch/clean |
| **L3 — Data, Quality & Platform** | Cosmos persistence, quality gates, blob storage, export/publish, Azure AI Search memory, observability, infra/CI | `IAzureStorageService`, `IAzureSearchService`, `ICosmosResultStore`, quality gates, export, OTel, Bicep |

> **Sync points** are called out per phase (the moments lanes must integrate). Outside those,
> lanes work against the Phase 0 contracts independently.

---

## 3. Recommended solution structure

To reduce merge conflicts with 3 engineers, introduce a **Core** project as the contract home.
Keep it light; do it once in Phase 0.

```
AgenticRagScanner.sln
 AgenticRagScannerApi/            # host: controllers, DI, Program.cs (exists)
 AgenticRagScanner.Core/          # NEW: domain models, enums, interfaces, SearchHistory, ResultItem
 AgenticRagScanner.Workflows/     # NEW: MAF workflow + agents (L1/L2)  [added in Phase 2]
 AgenticRagScanner.Tests/         # NEW: unit/contract/integration/eval tests
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
    `AppliesTo`, `DateConfidence`, level-of-authority, `Unverified`, `FullTextBlobUri` (blob reference
    to the persisted cleaned full text — Phase 5), `runId`, `groupId`, `version`
  - `Verdict`, `LevelOfAuthority` enums
- [ ] **(L1)** Add shared **throttle** abstraction (`ISharedThrottle`/`IRateLimiter`) — no real limits yet.
- [ ] **(L3)** Cross-cutting: OpenTelemetry skeleton (console exporter ok for now), options
  validation (`ValidateOnStart`), `/health` endpoint, `DefaultAzureCredential` registration.
- [ ] **(L3)** GitHub Actions CI (build + test + format).

**DoD / demo:** solution builds with Core + Tests; `/health` returns healthy; CI green; contracts
compile and are referenced by the API.

> **Sync point:** contracts reviewed & merged by all 3 before Phase 1. This is the most important
> gate in the plan.

---

### Phase 1 — Run lifecycle: synchronous scan · *L1-led*
**Goal:** accept the request, iterate the selected topic groups **sequentially** (one at a time), and **return the aggregated results in the HTTP response**. Synchronous for the POC — no run-status/background machinery. *(Parallel fan-out is deferred to Phase 12; the async path is captured in Phase 13.)*
`-> arch steps 1–2`

**Tasks**
- [ ] **(L1)** `IScanOrchestrator` + implementation: map `ScanRequest` -> one `TopicGroupContext`
  per group (each seeded with an empty `SearchHistory`), then run them **one at a time**.
- [ ] **(L1)** Per-group **placeholder** step (returns a stub result) so **sequential per-group** progress is observable; Phase 2 swaps it for the real MAF workflow.
- [ ] **(L1)** Wire `ScannerController.Scan` to run the scan **synchronously** and return `200` with the aggregated results (replace TODO).
- [ ] **(L1)** Wire the shared throttle for outbound LLM/Bing rate-limiting (concurrency capping deferred to Phase 12).
- [ ] **(L3)** Structured logging scopes (`runId`, `topicGroupId`) around the sequential per-group loop.

**DoD / demo:** `POST scan` with 3 topic groups -> `200` with aggregated stub results; the 3 groups complete one-by-one. No status endpoint, no MAF yet.

> **Sync point:** end of Phase 1 the **synchronous run harness is ready**. L2 and L3 can build agents/data against the contracts largely independently.

---

### Phase 2 — MAF workflow scaffolding (stub agents) · *L1 + L2*
**Goal:** introduce **Microsoft Agent Framework** and build the **agentic RAG loop** per group with
**all agents + steps present but stubbed** (canned, schema-valid outputs). `? arch step 3 (loop), 4–14 as stubs`

#### Agents vs. deterministic steps (important)
You're right that the loop is built from **MAF agents** — but **not every node is an LLM agent**, and
the agents are **hosted two different ways**. The design-doc diagram marks five **"Agent"** nodes
(LLM-backed); the rest are **deterministic tools/steps** the workflow calls between agents. Getting
this split right keeps cost down and makes each unit independently testable.

- **Foundry agent (1):** **Web Search (5)** is the solution's single **Foundry agent** (a hosted agent
  defined in the Foundry project) configured with the **Grounding with Bing Custom Search** tool. MAF
  **references** this agent; it takes the synthesized queries and **performs the grounded,
  allowlist-scoped web search** via its tool, returning hits/citations.
- **MAF agents over a Foundry model deployment (5):** Query Synthesis (4), Relevance Eval (9),
  Enrichment (12), Categorize (13), Summarize & Impact (14) are plain **MAF agents** that only
  **reference a Foundry project + model deployment** (a chat client) — no hosted agent, no tools.
  **Query Synthesis (4)** turns the request's topic groups into focused Bing **query strings** (it does
  *not* call Bing itself; the Web Search Foundry agent does).
- **Deterministic steps (not agents):** Pre-filter (6); Fetch & Clean (7); Loop Controller (10);
  Verdict Routing (11).

#### The agent roster (build each as a stub in this phase, fill in later)

| # | Agent (LLM) | Role in the workflow | Input ? Output (contract) | Real impl. phase |
|---|-------------|----------------------|---------------------------|------------------|
| 4 | **Query Synthesis Agent** *(MAF agent — Foundry model deployment)* | Turn the topic-group keyword/synonym OR-list into focused search quer(ies); on re-loops read `SearchHistory` to rotate synonyms / fill gaps. Decides *how many* queries. **Returns query strings only — the Web Search agent (5) runs Bing.** | `TopicGroupContext` + `SearchHistory` ? `string[] queries` | **3** |
| 5 | **Web Search Agent** *(Foundry agent — Grounding with Bing Custom Search tool)* | Execute the Query Synthesis agent's queries via the **Grounding with Bing Custom Search** tool (allowlist-scoped); return grounded hits/citations. | `string[] queries` (from agent 4) ? grounded allowlisted hits/citations | **4** |
| 9 | **Relevance Eval Agent** | Single full-text call ? `RELEVANT/BORDERLINE/NOT_RELEVANT`; effective-date aware; applies retrieved learnings; judges goal coverage. | cleaned full text + dates + `SearchHistory` ? `Verdict` + date fields + rationale | **6** |
| 12 | **Enrichment Agent** | Post-verdict enrichment only (relevance already decided): `whatItDoes` summary + metadata. | carried `ResultItem` ? enriched `ResultItem` | **7** |
| 13 | **Categorize Agent** | Assign impact area, regulator, and **approved tags only** (controlled vocabulary). | enriched `ResultItem` ? category fields | **7** |
| 14 | **Summarize & Impact Agent** | RAG over in-memory history ? plain-English impact summary + effective-date framing. | enriched `ResultItem` + `SearchHistory` ? summary/impact | **7** |

> The two controller nodes — **Loop Controller (10)** and **Verdict Routing (11)** — are deterministic
> orchestration owned by **L1**, not agents. **Web Search (5) is a distinct node:** the single
> **Foundry agent** carries the **Grounding with Bing Custom Search** tool, takes the Query Synthesis
> agent's queries, and returns grounded, allowlist-scoped hits/citations that flow straight into the
> deterministic pre-filter (6).

**Tasks — workflow & orchestration (L1)**
- [ ] Add `AgenticRagScanner.Workflows`; define **one MAF workflow per topic group**, with MAF
  **Cosmos checkpointing** wired to the shared Azure Cosmos account (see Phase 8) so long runs are durable/resumable.
- [ ] Build the loop scaffold threading `SearchHistory` through each pass:
  `QuerySynthesis (MAF agent) ? WebSearch (Foundry agent, Grounding with Bing Custom Search) ? Pre-filter ? Fetch&Clean ?`
  `RelevanceEval ? LoopController ? VerdictRouting ? Enrichment ? Categorize ? Summarize&Impact`.
- [ ] **Loop Controller** stub (deterministic): honor per-group `maxLoops` (default 3); append each pass to `SearchHistory`.
- [ ] **Verdict Routing** stub (deterministic): RELEVANT/BORDERLINE ? enrichment; NOT_RELEVANT ? dropped + logged.
- [ ] Stub the **Web Search Foundry agent** (Grounding with Bing Custom Search tool, allowlist-scoped) — takes queries, returns canned grounded hits. It is a **distinct node** between Query Synthesis and Pre-filter.

**Tasks — agent stubs (L2)** *(one PR per agent — naturally parallel across the team)*
- [ ] **Query Synthesis Agent** stub (**MAF agent** over the model deployment) — returns 1–2 canned **queries** (no Bing call).
- [ ] **Web Search Agent** stub (**the single Foundry agent** w/ Grounding with Bing Custom Search tool) — takes queries, returns canned grounded hits/citations.
- [ ] **Relevance Eval Agent** stub — returns a canned `Verdict` + date fields per item.
- [ ] **Enrichment Agent** stub — returns a canned `whatItDoes` + metadata.
- [ ] **Categorize Agent** stub — returns canned impact area / regulator / approved tags.
- [ ] **Summarize & Impact Agent** stub — returns a canned plain-English summary.
- [ ] For each stub: define its agent (Query Synthesis + the four downstream stubs are **MAF agents
  over a Foundry model deployment**; **Web Search is the single Foundry agent** with the Grounding with
  Bing Custom Search tool) — name, instructions placeholder, I/O type — register it in DI, and add a
  `Prompts/<Agent>Prompt.cs` placeholder (see prompt-management standard).

**DoD / demo:** a run executes the **entire loop end-to-end with fake data**, loops up to `maxLoops`,
routes verdicts, emits stub `ResultItem`s, and **checkpoints to Cosmos**. No external LLM/Bing calls yet.

> **Sync point:** the **agent I/O contracts** (table above) are frozen here. From this point each
> downstream phase swaps **one stub for a real implementation** — so the 3 engineers can each own
> different agents in parallel (e.g. L2a: Query Synthesis+Eval; L2b: Enrichment/Categorize/Summarize)
> without colliding.

---

### Phase 3 — Foundry model deployment + Query Synthesis Agent (first real agent) · *L2-led*
**Goal:** make LLM calls real via the **Foundry model deployment**; implement the **Query Synthesis Agent** (a MAF agent that synthesizes query strings) as the first real agent. `? arch step 4`

**Tasks**
- [x] **(L2)** Implement `IFoundryService` against a **Microsoft Foundry project + model deployment**
  using `DefaultAzureCredential` (prefer an `IChatClient` abstraction via `Microsoft.Extensions.AI`);
  add resilience + throttle. **This is the chat client the five MAF agents reference** (Query Synthesis,
  Relevance Eval, Enrichment, Categorize, Summarize & Impact) — they need only the project + deployment, no hosted agent.
  *(Shared `IChatClient` registered in DI, built from `AzureOpenAIClient` + `ResilientChatClient` (Polly retry/timeout + shared throttle + token/latency logging) and OpenTelemetry; `FoundryService` now delegates to it.)*
- [x] **(L2)** Prompt management: externalized, versioned prompt templates. *(`QuerySynthesisPrompt` v1 + `docs/prompt-management.md`.)*
- [x] **(L2)** **Query Synthesis Agent** (real) — implement as a **MAF agent over the Foundry model
  deployment** (the `IChatClient` above): synthesize focused **query strings** from the topic-group
  keyword set; on re-loops consult `SearchHistory` to **rotate synonym coverage** and avoid redundancy
  (primer §2/§3). It returns queries only — the Web Search Foundry agent (Phase 4) runs Bing.
  *(`QuerySynthesisAgent` over a MAF `ChatClientAgent`.)*
- [x] **(L2)** Structured output + validation; bounded retry on invalid JSON. *(JSON response format, tolerant parse + dedupe/cap, bounded retry, deterministic fallback.)*
- [x] **(L3)** Token-usage + latency metrics for the agent. *(Per-call token/latency logged in `ResilientChatClient`; OpenTelemetry GenAI instrumentation wired for export in Phase 11.)*

**DoD / demo:** real, non-redundant **queries** generated from a topic group's keywords; second loop
targets untested synonyms/gaps. (Grounded hits arrive once the Web Search Foundry agent is real in Phase 4.)

---

### Phase 4 — Web Search Foundry agent (Grounding with Bing Custom Search) + deterministic pre-filter · *L2 + L1/L3*
**Goal:** stand up the **Web Search Foundry agent** — the solution's single Foundry agent, with the
**Grounding with Bing Custom Search** tool — that **executes the Query Synthesis agent's queries**
(allowlist-scoped), then add the deterministic pre-filter.
`? arch steps 5–6` *(step 5 is its own Foundry agent — the Web Search agent — not a standalone Bing service)*

**Tasks**
- [ ] **(L2)** Implement the **Web Search Agent** (real) as the single **Foundry agent** (defined in
  the Foundry project) with the **Grounding with Bing Custom Search** tool, **referenced by the MAF
  workflow**: it takes the Query Synthesis agent's queries and returns grounded hits/citations.
- [ ] **(L2)** Configure the **Grounding with Bing Custom Search** resource/connection: scope the
  custom-search instance to the **primary-source allowlist** so grounding is allowlist-restricted
  (primer §2). *(Supersedes the standalone `IBingSearchGroundingService` /
  `IBingCustomSearchGroundingService` — grounding is owned by the Foundry agent's tool.)*
- [ ] **(L2)** Verify the agent returns grounded hits/citations restricted to allowlisted domains;
  tune the tool's result count / configuration.
- [ ] **(L1/L3)** **Deterministic pre-filter:** dedupe (incl. **cross-group**), URL
  reachability/validity. Pure functions, fully unit-tested.

**DoD / demo:** the Web Search Foundry agent returns real results restricted to allowlisted domains via
its Bing Custom Search tool; duplicates (including across groups) removed; dead URLs dropped.

---

### Phase 5 — Full-text fetch & clean + blob storage · *L3 + L2*
**Goal:** fetch & clean source content; persist artifacts. `? arch step 7`

**Tasks**
- [ ] **(L3)** Implement `IAzureStorageService.UploadBlobAsync` (BlobServiceClient +
  `DefaultAzureCredential`); containers from options.
- [ ] **(L2)** Fetch HTML/PDF, **strip boilerplate**; cleaned full text is held **in-memory** and
  passed to the Relevance Eval agent (Phase 6); on failure **fall back to Bing summary +
  flag `Unverified`** (do not drop — primer §3). Fetch & clean **never discards** on relevance —
  discard happens only at eval (Phase 6).
- [ ] **(L3)** **Fetch hygiene (not a full SSRF guard):** http/https-only scheme + caps on response
  size, redirects, content-types, and per-fetch timeout. The full SSRF guard (host allowlist +
  private/loopback IP blocking) is **deferred** — fetch targets are the customer's curated
  primary-source (government) domains, already gated by Bing Custom Search. Tracked as an Epic 11
  nice-to-have (backlog 11.6).
- [ ] **(L3)** **Persist cleaned full text to blob (mandatory, audit).** Every fetched result URL's
  cleaned full text is stored to blob (the live URL can change or 404, so snapshot exactly what eval
  read). Store a **blob reference (path/URI)**, on `ResultItem.FullTextBlobUri`; keep the
  container **private** and mint a short-lived **user-delegation SAS** on demand (or stream via RBAC) at
  view time. Use a deterministic, idempotent key (e.g. `fulltext/{runId}/{groupId}/{itemId}.txt`).

> **Data-retention decision (resolved).** Full text **is** persisted — audit/provenance require an
> immutable snapshot of what the agent read. It is stored in **blob, referenced** from the `ResultItem`,
> **not inline** in Cosmos: Cosmos has a **2 MB item cap** (large HTML/PDF would hard-fail) and charges RU
> proportional to body size on every read/list. The structured decision (`Verdict`, `EvalRationale`,
> dates, `SourceUrls`, `FullTextBlobUri`) lives on the Cosmos `ResultItem` (Phase 8); the bytes live in
> blob. The in-memory `SearchHistory`/`FetchedDocument` remain transient loop state.

**DoD / demo:** cleaned full-text persisted to blob for every result URL and referenced on the
`ResultItem`; unreachable docs produce an `Unverified` item via summary fallback.

---

### Phase 6 — Relevance eval (3-verdict, date-aware) + real loop controller · *L2 + L1*
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

**Loop-feedback signal (design decision — build here, not before):** the eval agent must emit a
**steer** for the next query-synthesis pass that distinguishes **two reasons to loop again**, because
each wants a different next query:
1. **Missing facet** — an aspect of the theme was never retrieved → next query **broadens** to introduce it.
2. **Insufficient evidence** — the facet *was* covered, but the source is thin / secondary /
   low-authority / out-of-date / ambiguous → next query **deepens or pivots** on the *same* facet
   toward an authoritative primary source.

**Two implementation options (decide at build time):**
- **(A) Prose reasoning.** The eval prompt is instructed to write its `ThoughtProcess` in a defined
  shape that names both kinds explicitly (e.g. *"Missing facets: X, Y — the next query must include
  these. Facets P, Q are represented but evidence is weak because <reasons> — search for supplemental
  authoritative sources on these."*). The v4 query-synthesis prompt **already consumes
  `SearchHistory.Reviews`**, so this needs **no Core contract change** — but the query-synthesis prompt
  must be extended with **explicit instructions on how to interpret that reasoning** (what the "Missing
  facets" vs "weak evidence" phrasing means and how each should change the query). Cheapest path;
  weaker guarantees (free text can drift).
- **(B) Structured directives.** Add `IReadOnlyList<SearchDirective> Directives` to `ReviewDecision`,
  each with **`Facet`** + a **`Reason`** enum (`MissingFacet | WeakEvidence | LowAuthority | Stale |
  Ambiguous`) + optional **`Note`**. Stronger, machine-checkable, lets metrics count directive types.
  Requires a Core contract change **and** the query-synthesis prompt must document **how the directives
  are formatted and how to interpret each `Reason`** (render `MissingFacet` as a "broaden / introduce
  these terms" instruction and the insufficiency reasons as a "deepen / find primary sources for this
  facet" instruction).

**Either way, the contract is shared:** whatever the eval emits (prose shape *or* directive list), the
query-synthesis prompt must carry matching **interpretation instructions**, and the two must be versioned
together so a change on one side never silently desyncs the other.

**Decision:** Phase 6 ships **Option (A)** — the lower-risk prose route, no Core contract change. **Option
(B)** (the structured `SearchDirective` list) is deferred as a **later-phase nice-to-have** (see Phase 11
tuning), to be picked up only if eval prose proves unreliable in practice or once directive-type metrics
are wanted. Do **not** add the `SearchDirective` Core field in Phase 5 or Phase 6 — there is no producer
for it until (B) is actually scheduled.

**DoD / demo:** items classified with verdicts + dates; loop exits per the rules; BORDERLINE items
flagged and carried; NOT_RELEVANT logged.

---

### Phase 7 — Enrichment + Categorize + Summarize/Impact agents · *L2-led*
**Goal:** finish the downstream agents. `? arch steps 12–14`

**Tasks**
- [ ] **(L2)** **Content Analysis / Enrichment** (real): `WhatItDoes` summary; enrich metadata.
- [ ] **(L2)** **Categorize Agent** (Stage 2): impact area, regulator, **approved tags only**.
- [ ] **(L2)** **Summarize & Impact Agent** (Stage 3): RAG over in-memory history; effective date;
  **plain-English** impact.

**DoD / demo:** each carried item is enriched, categorized with approved tags, and has a
plain-English impact summary.

---

### Phase 8 — Deterministic quality gates + Cosmos persistence · *L3-led*
**Goal:** validate, dedupe vs store, stamp authority, persist. `? arch steps 15–16`

**Tasks**
- [ ] **(L3)** **Quality gates** (non-LLM): JSON-schema validation; dedupe **vs Cosmos**;
  **level-of-authority stamping** (legislation > court ruling > HMRC guidance).
- [ ] **(L3)** `ICosmosResultStore` (Microsoft.Azure.Cosmos + `DefaultAzureCredential`):
  **one versioned doc per item per run**; partition-key strategy (e.g. by jurisdiction or `runId`);
  **idempotent** upsert (ETag/optimistic concurrency). The doc carries the **`FullTextBlobUri`
  reference** (Phase 5), not the full text body — the bytes stay in blob (2 MB item cap + RU cost).
- [ ] **(L3)** Reuse the **same Azure Cosmos account as the MAF checkpoint store** (see Phase 2) —
  separate containers for `checkpoints` vs `results`; one account, no emulator.

**DoD / demo:** results persisted in Cosmos as versioned docs; re-running a run does not duplicate;
authority level stamped.

---

### Phase 9 — Publish & export · *L3-led*
**Goal:** publish results and export. `? arch step 17`

**Tasks**
- [ ] **(L3)** Auto-publish published-update view from Cosmos.
- [ ] **(L3)** **CSV/Excel export** to blob; expose a download/link endpoint.

**DoD / demo:** completed run produces a downloadable CSV/Excel of the published updates.

---

### Phase 10 — Memory / learnings store (Azure AI Search) · *L3 + L2* · *was FUTURE #8*
**Goal:** cross-run learnings feeding synthesis + eval. `? arch step 8 (planned)`

**Tasks**
- [ ] **(L3)** Implement `IAzureSearchService` (Azure.Search.Documents + `DefaultAzureCredential`):
  index for curated learnings; vector/hybrid retrieval.
- [ ] **(L2)** Feed retrieved learnings into Query Synthesis + Relevance Eval (RAG); distinct from
  the per-run in-memory `SearchHistory`.

**DoD / demo:** prior-run learnings are retrieved and demonstrably influence queries/eval.

---

### Phase 11 — Hardening: evals, throttle tuning, dashboards, security · *all lanes*
**Goal:** make it production-credible.

**Tasks**
- [ ] **(L2)** Formal **eval suite** (relevance/groundedness/recall) on the golden dataset; CI-gated.
- [ ] **(L1)** Load/throttle tuning to stay within TPM/RPM/QPS; backpressure verified.
- [ ] **(L3)** App Insights **dashboards** (latency, tokens, verdict mix, failures); alerts.
- [ ] **(All)** Security review (SSRF, secrets hygiene, least-privilege RBAC roles).
- [ ] **(L2, nice-to-have)** **Loop-feedback Option (B):** upgrade the eval→query-synthesis steer from
  Phase 6's prose (Option A) to a structured `IReadOnlyList<SearchDirective>` on `ReviewDecision`
  (`Facet` + `Reason` enum + `Note`), with matching interpretation instructions in the query-synthesis
  prompt and the two prompts versioned together. Only pursue if Phase 6 prose proves unreliable or
  directive-type metrics are wanted. (See Phase 6.)

**DoD / demo:** eval scores tracked over time; dashboards live; documented limits respected.

---

### Phase 12 — Fan-out & parallelization (MAF) · *L1-led*
**Goal:** now that the whole pipeline runs reliably **end-to-end and sequentially**, introduce
parallel per-topic-group execution under the shared throttle. Deferred here on purpose to de-risk
threading and keep the early phases focused on the core RAG loop.

**Tasks**
- [ ] **(L1)** Replace the sequential run loop (Phase 1) with `Task.WhenAll` gated by the shared
  throttle; **cap active workers**; preserve per-group isolation (one group failing does not abort the run).
- [ ] **(L1)** Per-group cancellation still honored under parallel execution; partial results preserved.
- [ ] **(L3)** Concurrency telemetry: in-flight concurrency gauge + throttle wait-time metric; parallel spans per run/group.
- [ ] **(L1)** Load/throttle tuning under parallel load: stay within TPM/RPM/QPS with N groups in flight; backpressure verified; per-group cap documented.

**DoD / demo:** the same pipeline that ran sequentially now runs topic groups **concurrently** under
the throttle; throughput improves; the throttle caps active workers; parallel spans visible; cancellation still works.

---

### Phase 13 — FUTURE / post-POC (not scheduled) · *backlog*
`? arch steps 18–20 + primer §5 deferrals`
- [ ] Azure **Function** timer host (scheduled scans) alongside the Web API.
- [ ] **Bicep** infra-as-code for all resources + Managed Identity role assignments.
- [ ] **Admin UI** — review of past runs.
- [ ] **Structured review capture** (verdict correction + reason codes + notes).
- [ ] **Distillation job** rolling reviews into curated guidance ? memory store (Phase 10).
- [ ] **Async execution mode** — background run + run-status store + `GET /runs/{runId}` polling + cancellation (merged out of the old Phase 2). Only needed if synchronous scans outgrow gateway timeouts (~230s on App Service) or a live-progress UI is wanted.

---

## 5. Dependency & parallelization map

```
Phase 0 (contracts) --> Phase 1 --> Phase 2 (skeleton) --> from here, parallel:
                                                  |
                                                  +-- L2 track: Phase 3 -> 4 -> 5 -> 6 -> 7
                                                  |
                                                  +-- L3 track: Phase 5 (storage) -> Phase 8 -> 9 -> 10
                                                  |
                                                  +-- L1 track: Phase 6 loop controller -> Phase 11 tuning
```

- **Serial spine (everyone depends on):** Phase 0 -> 1 -> 2.
- **After Phase 2**, the three lanes proceed largely in parallel:
  - **L2** drives the agent chain (3 -> 4 -> 5 fetch -> 6 -> 7).
  - **L3** drives storage (5) + persistence/export/memory (8 -> 9 -> 10), and can start storage early.
  - **L1** owns loop controller/verdict routing (6), the **synchronous run harness (1)**, and **fan-out/parallelization (12)** plus throttle tuning (11).
- **Integration sync points:** end of Phase 0 (contracts), end of Phase 2 (loop shape), and before Phase 8 (ResultItem schema final).

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

Once Phase 0 merges, branch **Phase 1** (`feat/phase-1-synchronous-scan`, L1-led) while L2/L3
begin prepping their tracks against the frozen contracts.
