# Epic 4 — Web Search agent (Foundry, Grounding with Bing Custom Search) + deterministic pre-filter

> **Phase:** `phase-4` · **Lanes:** L2 (agents/grounding) + L1/L3 (deterministic pre-filter)
> **Companion docs:** [`docs/backlog.md`](backlog.md) (Epic 4) · [`docs/implementation-plan.md`](implementation-plan.md) (Phase 4) ·
> [`docs/architecture-context.md`](architecture-context.md) · [`docs/horizon-scanner-architecture.md`](horizon-scanner-architecture.md) ·
> [`docs/prompt-management.md`](prompt-management.md)
>
> **Goal.** Replace the canned [`BingSearchTool`](../AgenticRagScannerApi.Workflows/Tools/BingSearchTool.cs)
> with the solution's single **Web Search agent** — a **pre-provisioned Foundry agent** (created in the
> portal with the **Grounding with Bing Custom Search** tool), scoped to the primary-source allowlist and
> **resolved by name** by the MAF workflow — then add the real **deterministic pre-filter** (cross-group
> dedupe + URL validity).
>
> **Global DoD** ([`implementation-plan.md` §1](implementation-plan.md)) applies on top of every story's own criteria.

> **Superseded (phase-7).** This plan was written when the per-group loop ran inside a single
> `TopicGroupPipeline` (called by a self-looping `TopicGroupLoopExecutor`). In phase-7 the loop body was
> decomposed into **seven MAF executors** (`QuerySynthesis → WebSearch → PreFilter → FetchAndClean →
> RelevanceEval → LoopController → Finalize`) and both `TopicGroupPipeline` and the monolithic executor
> were **removed**. Read the `TopicGroupPipeline.RunPassAsync` / `IWebSearchAgent`-seam references below as
> historical: the `IWebSearchAgent` seam still exists, but it is now consumed by `WebSearchExecutor`
> rather than by a pipeline. See [`docs/maf-executor-design.md`](maf-executor-design.md) for the as-built design.

---

## 1. Scope (from the backlog)

| Story | Title | Lane | Depends on |
|-------|-------|------|------------|
| **4.1** | Web Search agent (Foundry agent w/ Grounding with Bing Custom Search), allowlist-scoped | L2 | 2.4, 3.3 *(merged 4.1 + 4.2)* |
| **4.3** | Deterministic pre-filter (dedupe incl. cross-group + URL validity) | L3 | 0.3 |

**Epic demo:** the Web Search agent executes the synthesized queries and returns real **allowlisted**
results/citations via Grounding with Bing Custom Search; duplicates (including **cross-group**) are
removed; dead/invalid URLs are dropped.

**Out of scope (later epics):** full-text fetch & clean + the SSRF guard (Epic 5); real relevance eval
(Epic 6); parallel fan-out across groups (Epic 12). The pre-filter here stays a pure/deterministic step.

---

## 2. Current state (what exists today)

- The per-group loop in `TopicGroupPipeline.RunPassAsync` *(removed in phase-7)*
  runs `QuerySynthesis → WebSearch → PreFilter → Fetch&Clean → RelevanceEval → LoopController`.
- **Query Synthesis** is the first real agent (Epic 3): [`QuerySynthesisAgent`](../AgenticRagScannerApi.Workflows/Agents/QuerySynthesisAgent.cs)
  — a MAF `ChatClientAgent` over the shared Foundry `IChatClient`. It returns **one query string per pass**.
- **Web Search** is still a stub: [`BingSearchTool`](../AgenticRagScannerApi.Workflows/Tools/BingSearchTool.cs)
  returns three canned `SearchHit`s and only has an allowlist *hook* (`run.AuthoritativeSources`).
- **Pre-filter** is a scaffold: [`PreFilterStep`](../AgenticRagScannerApi.Workflows/Steps/PreFilterStep.cs)
  dedupes against the **per-group** [`SearchHistory.ProcessedKeys`](../AgenticRagScannerApi.Core/Runtime/SearchHistory.cs)
  and drops non-http(s) URLs. No **cross-group** dedupe and no reachability check yet.
- The Foundry chat client is wired in [`ServiceCollectionExtensions.AddFoundryChatClient`](../AgenticRagScannerApi/Extensions/ServiceCollectionExtensions.cs)
  directly against the Azure OpenAI inference endpoint ([`FoundryOptions`](../AgenticRagScannerApi/Configuration/FoundryOptions.cs))
  — there is **no `AIProjectClient`** yet, which Epic 4 needs to reference a hosted Foundry agent.
- [`ScanOrchestrator`](../AgenticRagScannerApi/Orchestration/ScanOrchestrator.cs) currently seeds
  `RunContext.AuthoritativeSources = []` (TODO: the allowlist now lives in the Bing Custom Search config).

---

## 3. Key design decision — how the Web Search agent is hosted (use the *current* APIs)

The grounding tool is a **server-side hosted tool**. In the .NET Agent Framework the standard
`ChatClientAgent` (Responses) tool surface **does not** include Bing Custom Search — hosted grounding
tools must be attached to a **server-managed (versioned) Foundry agent definition**, then **referenced**
from code. So the Web Search node is a **`FoundryAgent`**, not a `ChatClientAgent`.

> ⚠️ **Use the GA Foundry Agent Service path, not classic agents.** The `Azure.AI.Agents.Persistent`
> `BingCustomSearchTool` flow is **Foundry (classic)** and is **deprecated — retiring 2027-03-31**.
> Build on the **Microsoft Foundry Agents Service** + **Microsoft Agent Framework** Foundry integration.

### Packages (confirm exact versions at implementation time — pin to the repo's MAF line)

| Package | Why | Notes |
|---------|-----|-------|
| `Microsoft.Agents.AI.Foundry` | `AIProjectClient.AsAIAgent(...)` → MAF `AIAgent`; lets MAF reference the pre-provisioned agent | Align with the repo's `Microsoft.Agents.AI` **1.10.0** line (latest stable today is `1.5.0`; a matching/newer build may be required — verify on restore). |
| `Azure.AI.Projects` | `AIProjectClient` + `AgentAdministrationClient` (create/get versioned agent), `Connections` (resolve the Bing connection id) | Latest stable **2.0.1** (newer prerelease exists). Requires a **project endpoint** (hub-based projects discontinued). |
| `Azure.Identity` | already referenced — `DefaultAzureCredential` (keyless) | — |

### Creating the hosted agent definition (portal-first — the approach used)

The Web Search agent is **created once in the Foundry portal**, with the **Grounding with Bing Custom
Search** tool attached to the connection + configuration instance. Code never constructs the tool or the
agent definition — it only **resolves the agent by name** (optionally a pinned version) and runs it. The
allowlist lives entirely in the Foundry configuration instance.

> *Alternative (not used): code-first creation via
> `AIProjectClient.AgentAdministrationClient.CreateAgentVersion(agentName, new(DeclarativeAgentDefinition{ … }))`
> with a `bing_custom_search` tool whose `search_configurations[]` carry `connection_id` + `instance_name`.
> Better for IaC later (Epic 13.2), but adds moving parts the POC doesn't need.*

### Referencing it from MAF

```csharp
var project = new AIProjectClient(new Uri(options.ProjectEndpoint), credential);

AIAgent agent;
if (string.IsNullOrWhiteSpace(options.AgentVersion))
{
    // Latest version resolved automatically by name.
    ProjectsAgentRecord record = project.AgentAdministrationClient.GetAgent(options.AgentName);
    agent = project.AsAIAgent(record);
}
else
{
    // Pin a specific published version.
    ProjectsAgentVersion version = project.AgentAdministrationClient.GetAgentVersion(options.AgentName, options.AgentVersion);
    agent = project.AsAIAgent(version);
}
```

The hosted agent owns its tools/instructions; per-run options (instructions, tools) passed client-side
are ignored. Send the **synthesized query** as the run input and read **URL-citation annotations** off the
response to build `SearchHit`s.

---

## 4. Story 4.1 — Web Search agent (Foundry + Grounding with Bing Custom Search)

### 4.1.a Infrastructure / portal prerequisites (one-time, document in README + `appsettings.*.example`)

- [ ] Create a **Grounding with Bing Custom Search** resource in the **same resource group** as the
  Foundry project (Owner/Contributor required).
- [ ] Create a **configuration instance** whose **allowed domains** ARE the primary-source allowlist
  (`legislation.gov.uk`, `gov.uk/hm-revenue-customs`, `supremecourt.uk`, … — see
  [`horizon-scanner-architecture.md`](horizon-scanner-architecture.md)). **This config is the allowlist gate
  — enforced at query time.** Record the **instance name**.
- [ ] Add the resource as a **project connection**; record the **connection id/name**.
- [ ] Create/define the **Web Search agent** (portal or code) with the `bing_custom_search` tool bound to
  that connection + instance, over the existing model deployment.
- [ ] **RBAC:** grant the app's identity (`DefaultAzureCredential` / Managed Identity) the role required to
  run project agents (e.g. **Azure AI User**) on the Foundry project. Keyless; no secrets in source.
- [ ] **Security note:** the Bing grounding tool calls a **public endpoint** and sends the query outside
  the Azure compliance boundary even from a network-secured Foundry — capture this in the security review (11.4).

### 4.1.b Configuration (Options pattern + `ValidateOnStart`)

- [ ] Add **`WebSearchOptions`** under `AgenticRagScannerApi.Workflows/Configuration/` (section `"WebSearch"`),
  validated with data annotations and bound in
  [`AddConfiguredOptions`](../AgenticRagScannerApi/Extensions/ServiceCollectionExtensions.cs):

  | Property | Purpose |
  |----------|---------|
  | `ProjectEndpoint` *(Required, Url)* | Foundry **project** endpoint for `AIProjectClient` (distinct from `FoundryOptions.Endpoint`, which is the AOAI inference endpoint). |
  | `AgentName` *(Required)* | Pre-provisioned Web Search agent name (resolved from the portal). |
  | `AgentVersion` *(optional)* | Pin a published version; omit to resolve latest. |
  | `MaxResults` *(Range)* | Cap `SearchHit`s returned per query (token/cost control). |
  | `MaxRetries` / `RetryBaseDelaySeconds` / `RequestTimeoutSeconds` | Resilience-pipeline tuning for the agent run. |

  *The agent's model, instructions, and tool parameters (count/market/freshness/…) are owned by the
  portal-provisioned agent — they are intentionally **not** client-side options.*

- [ ] Keep the **allowlist source of truth in the Bing configuration instance**. `RunContext.AuthoritativeSources`
  becomes **verification metadata** (used by tests/assertions that citations stay on-allowlist), not the gate.

### 4.1.c Code — the Web Search agent behind the existing pipeline seam

The pipeline calls `IWebSearchAgent.SearchAsync(query, run, ct) → IReadOnlyList<SearchHit>`. **Keep this
seam** so `TopicGroupPipeline` is
unchanged. (The seam was renamed `IBingSearchTool` → `IWebSearchAgent` for clarity.)

- [ ] **Register `AIProjectClient`** as a singleton in
  [`AddAzureSdkClients`](../AgenticRagScannerApi/Extensions/ServiceCollectionExtensions.cs) (keyless via the
  shared `TokenCredential`).
- [ ] **Resolve the `AIAgent` once** (singleton/lazy) from `AIProjectClient.AsAIAgent(GetAgent(name))`
  — agent metadata is stable for the run; don't fetch per query.
- [ ] Implement **`WebSearchAgent : IWebSearchAgent`** (new file in `Workflows/Tools/`,
  replacing the canned `BingSearchTool` in DI):
  - Run the hosted agent with the **synthesized query** as input (one thread/run per query).
  - Parse **URL-citation annotations** → `SearchHit { Url, Title, Snippet, Domain, SourceQuery = query, Rank }`.
    `Domain = new Uri(url).Host`; `Snippet` from the citation text/annotation; `Rank` by citation order.
  - **De-dupe within the single response** and **cap at `MaxResults`** before returning.
  - **Throttle:** wrap the call in `ISharedThrottle.AcquireAsync()` (Bing QPS) —
    [`ISharedThrottle`](../AgenticRagScannerApi.Core/Throttling/ISharedThrottle.cs).
  - **Resilience:** retry-with-jitter + timeout on transient failures; honor `Retry-After` (Polly v8,
    consistent with [`ResilientChatClient`](../AgenticRagScannerApi/Services/ResilientChatClient.cs)).
  - **Observability:** log `runId`/`topicGroupId`/`query`, hit count, and tool-call count; emit latency.
  - **Defensive default:** on zero citations, return an empty list (the loop continues; never throw to abort the run).
- [ ] **Allowlist verification (defense-in-depth):** even though Bing config gates domains, drop any returned
  citation whose host is not on `run.AuthoritativeSources` when that list is supplied, and **log** a warning
  (detects misconfigured Bing instances). This is belt-and-suspenders, not the primary gate.
- [ ] **DI swap** in [`AddWorkflowServices`](../AgenticRagScannerApi/Extensions/ServiceCollectionExtensions.cs):
  register `IWebSearchAgent` via a factory that builds the `AIProjectClient`, resolves the pre-provisioned
  agent by name (latest unless `AgentVersion` is pinned), and constructs `WebSearchAgent` (remove the stub registration).
- [ ] **Supersede** the standalone grounding-service abstractions: confirm no
  `IBingSearchGroundingService` / `IBingCustomSearchGroundingService` remain referenced — grounding is now
  owned by the Foundry agent's tool (per the backlog note).

> **Prompt management:** a hosted/versioned agent's instructions live in Foundry, **not** in a
> `Prompts/*.cs` class. If you author the instructions in code (code-first creation), still record them in a
> versioned `Prompts/WebSearchAgentPrompt.cs` with a `Version` tag for parity with
> [`prompt-management.md`](prompt-management.md) and eval attribution (Epic 11). Document this exception.

### 4.1.d Tests (4.1)

- [ ] **Contract test** for `IWebSearchAgent` against a fake `AIAgent`/`AIProjectClient` that returns a
  canned response with URL-citation annotations → assert correct `SearchHit` mapping, dedupe, `MaxResults` cap.
- [ ] **Allowlist test:** off-allowlist citation is dropped + warning logged when `AuthoritativeSources` set.
- [ ] **Resilience/throttle test:** transient failure retried; `ISharedThrottle.AcquireAsync` invoked once per call.
- [ ] **Options validation test** (extend [`OptionsValidationTests`](../tests/AgenticRagScannerApi.Tests/OptionsValidationTests.cs)):
  missing `ProjectEndpoint`/`AgentName` fails at startup.
- [ ] *(Optional, manual/gated)* a **live smoke test** behind an env flag that hits the real agent and asserts
  on-allowlist citations — keep out of the default CI run (no network/secret dependency in PR CI).

---

## 5. Story 4.3 — Deterministic pre-filter (cross-group dedupe + URL validity)

Keep the pre-filter **pure and unit-tested**. Split "pure" logic (validity + dedupe) from any network I/O
(reachability), so the deterministic core has no external dependencies.

### 5.1 Cross-group dedupe (the new requirement)

Today dedupe is per-group (`SearchHistory.ProcessedKeys`). Add a **run-level** seen-set so the **same URL is
never fetched/evaluated twice across topic groups** (the dominant cost driver — see
[`implementation-plan.md` §1 “Cost controls”](implementation-plan.md)).

- [ ] Add a **run-scoped, thread-safe** URL registry. Two viable shapes — pick the lighter one:
  - **(A)** A `ConcurrentDictionary`-backed set on `RunContext` (e.g. `RunContext.SeenUrlKeys`) in Core. Simple;
    already reachable via `TopicGroupContext.Run`. Thread-safe so it's correct under Epic 12 parallel fan-out.
  - **(B)** A small run-scoped `IUrlDedupeRegistry` service injected into the pre-filter. Cleaner seam/testing.
- [ ] **Normalization** is the dedupe key (reuse/centralize the existing `NormalizeUrl`): absolute http(s)
  only, lowercase host+path, strip trailing `/`. Consider also stripping tracking query params / fragments and
  `www.` for stronger cross-group matching — **document the canonicalization rules** (they affect recall).
- [ ] Pre-filter checks **both** the per-group `SearchHistory.ProcessedKeys` **and** the run-level registry;
  a URL is kept only if it is new to **both**, then recorded in both.
- [ ] **Signature:** extend `IPreFilterStep.Filter` to take the run-level registry (or the `TopicGroupContext`,
  which exposes both `History` and `Run`). Update the single caller in `TopicGroupPipeline.RunPassAsync`.

### 5.2 URL validity + reachability

- [ ] **Validity (pure):** keep/strengthen the existing checks — absolute URL, `http`/`https` scheme, parseable
  host. Reject `mailto:`, `ftp:`, fragment-only, etc. Fully unit-tested, no I/O.
- [ ] **Reachability (injectable, optional for the POC):** drop **dead** URLs via a lightweight
  `IUrlReachabilityChecker` (HEAD, fall back to ranged GET; short timeout; capped redirects) backed by a
  resilient `HttpClient`. Keep it **separate** from the pure functions so the deterministic core stays unit-testable.
  - ⚠️ **Do not** implement the full **SSRF guard** here — that is **Epic 5** (allowlist enforcement on fetch,
    private/loopback IP blocking, size/redirect/content-type caps). The reachability check here is a light
    "is this link dead?" filter; gate it behind a config flag so CI doesn't depend on the network.

### 5.3 Tests (4.3)

- [ ] **Pure dedupe** unit tests: same URL across **two groups** in one run is kept once; differing
  casing/trailing slash/`www.`/tracking params collapse to one key (per documented canonicalization).
- [ ] **Validity** unit tests: non-http(s), unparseable, and fragment-only URLs dropped.
- [ ] **Within-run, multi-pass** test: a URL seen on pass 1 is dropped on pass 2 (existing behavior preserved).
- [ ] **Reachability** contract test against a fake checker: dead URL dropped, reachable kept; feature-flag off ⇒ no I/O.
- [ ] Extend `TopicGroupPipelineTests` *(removed in phase-7)* so the
  pipeline threads the run-level registry correctly end-to-end.

---

## 6. Wiring the allowlist through the run

- [ ] Decide how `RunContext.AuthoritativeSources` is populated. For the POC the **gate** is the Bing Custom
  Search configuration instance, so either:
  - leave `AuthoritativeSources` empty and rely solely on Bing config (simplest), **or**
  - mirror the allowlist into config (`WebSearchOptions` or a new `AllowlistOptions`) and pass it into
    `RunContext` so the **verification** drop in §4.1.c can run. Recommended: mirror it, for defense-in-depth + tests.
- [ ] Update [`ScanOrchestrator`](../AgenticRagScannerApi/Orchestration/ScanOrchestrator.cs) to set
  `AuthoritativeSources` from that config instead of `[]`, and refresh the inline TODO comment.

---

## 7. Touched files (summary)

| Area | File(s) | Change |
|------|---------|--------|
| Web Search agent | `Workflows/Tools/WebSearchAgent.cs` *(new)* | Real `IWebSearchAgent` over the pre-provisioned Foundry `AIAgent`. |
| Web Search agent | [`Workflows/Tools/BingSearchTool.cs`](../AgenticRagScannerApi.Workflows/Tools/BingSearchTool.cs) | Remove/retire the stub (or keep as a test double). |
| Config | `Workflows/Configuration/WebSearchOptions.cs` *(new)* | Project endpoint, agent name/version, result caps, tool params. |
| Prompts *(if code-first)* | `Workflows/Prompts/WebSearchAgentPrompt.cs` *(new, optional)* | Versioned agent instructions for parity/eval. |
| Pre-filter | [`Workflows/Steps/PreFilterStep.cs`](../AgenticRagScannerApi.Workflows/Steps/PreFilterStep.cs), [`IPreFilterStep.cs`](../AgenticRagScannerApi.Workflows/Steps/IPreFilterStep.cs) | Cross-group dedupe + validity; signature carries run-level registry. |
| Pre-filter | `Workflows/Steps/UrlReachabilityChecker.cs` *(new, optional)* | Injectable dead-link check (not SSRF). |
| Core | [`Core/Runtime/RunContext.cs`](../AgenticRagScannerApi.Core/Runtime/RunContext.cs) | Run-level dedupe set **or** a new `IUrlDedupeRegistry`. |
| Pipeline | [`Workflows/Pipeline/TopicGroupPipeline.cs`](../AgenticRagScannerApi.Workflows/Pipeline/TopicGroupPipeline.cs) | Pass the run-level registry into the pre-filter. |
| DI | [`Api/Extensions/ServiceCollectionExtensions.cs`](../AgenticRagScannerApi/Extensions/ServiceCollectionExtensions.cs) | Register `AIProjectClient`, resolve the pre-provisioned agent, swap `IWebSearchAgent`, bind `WebSearchOptions`. |
| Orchestration | [`Api/Orchestration/ScanOrchestrator.cs`](../AgenticRagScannerApi/Orchestration/ScanOrchestrator.cs) | Populate `AuthoritativeSources` from config. |
| Project | [`Workflows/AgenticRagScannerApi.Workflows.csproj`](../AgenticRagScannerApi.Workflows/AgenticRagScannerApi.Workflows.csproj) | Add `Microsoft.Agents.AI.Foundry` + `Azure.AI.Projects`. |
| Config samples | `appsettings.Local.json.example`, README | Document the Bing resource, configuration instance, connection, agent name, RBAC. |
| Tests | `tests/AgenticRagScannerApi.Tests/*` | New web-search contract/allowlist/throttle tests + cross-group dedupe tests. |

---

## 8. Definition of Done (Epic 4)

- [ ] The Web Search agent runs the synthesized query through the hosted Foundry agent's **Grounding with
  Bing Custom Search** tool and returns **real, allowlist-restricted** hits/citations mapped to `SearchHit`s.
- [ ] Citations are verifiably limited to allowlisted domains; off-allowlist results are dropped + logged.
- [ ] The pre-filter removes duplicates **including across topic groups** in the same run, and drops
  invalid/dead URLs — as **pure, unit-tested** functions (reachability injectable + flagged).
- [ ] Calls flow through `ISharedThrottle` and the resilience pipeline; failures degrade gracefully (the loop
  continues; a single agent error never aborts the run).
- [ ] No standalone Bing grounding *service* abstraction remains; grounding is owned by the agent's tool.
- [ ] `dotnet build`, `dotnet test`, and `dotnet format --verify-no-changes` are green; new options validate on start.
- [ ] README/`*.example` document the Bing resource + configuration instance (allowlist) + connection + agent + RBAC.

**Epic demo:** `POST scan` with ≥2 topic groups → the Web Search agent returns real allowlisted results;
a URL surfaced by two groups is evaluated **once**; dead URLs are dropped; logs show per-group `runId`/`query`
and on-allowlist citations.

---

## 9. Risks & mitigations

| Risk | Mitigation |
|------|------------|
| **Classic-vs-new agent confusion.** Most search results still show the deprecated `Azure.AI.Agents.Persistent` `BingCustomSearchTool` flow. | Build on the **Foundry Agents Service** + `Microsoft.Agents.AI.Foundry` `FoundryAgent`. Verify against current docs; classic retires 2027-03-31. |
| **Package/version drift.** `Microsoft.Agents.AI.Foundry` latest stable (`1.5.0`) lags the repo's `Microsoft.Agents.AI` `1.10.0`. | Verify a compatible build on `dotnet restore`; pin explicitly. Don't mix incompatible MAF versions. |
| **Grounding is best-effort, allowlist lives in Bing config.** A misconfigured instance could leak off-allowlist domains. | Defense-in-depth host-filter + warning log; assert on-allowlist citations in (gated) live tests. |
| **Compliance boundary.** Bing grounding sends the query to a public endpoint outside Azure's compliance boundary. | Document in the security review (11.4); ensure queries carry no sensitive end-user data. |
| **Over-aggressive canonicalization** in cross-group dedupe could collapse distinct pages → false negatives (costly in compliance). | Document canonicalization rules; unit-test edge cases; keep it conservative (favor recall). |
| **Reachability vs SSRF scope creep.** | Keep reachability light + flagged; the real SSRF guard is Epic 5. |
| **Per-query thread/run latency** (one hosted-agent run per query). | One query per pass (already the design); throttle + resilience; revisit batching if needed. |

---

## 10. Suggested PR sequence

1. **PR-1 (L3, no new SDKs):** cross-group dedupe + URL validity + run-level registry + tests (story **4.3**).
   Ships value immediately and is independent of the Foundry wiring.
2. **PR-2 (L2):** add `Azure.AI.Projects` + `Microsoft.Agents.AI.Foundry`, register `AIProjectClient`,
   `WebSearchOptions`, and resolve the pre-provisioned agent by name.
3. **PR-3 (L2):** `WebSearchAgent : IWebSearchAgent` + DI swap + allowlist verification + tests (story **4.1**).
4. **PR-4 (L1/L3):** wire `AuthoritativeSources` from config; optional injectable reachability checker;
   README/`*.example` docs.
