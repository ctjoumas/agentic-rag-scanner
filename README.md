# Agentic RAG Scanner

A regulatory **horizon-scanning** reference solution for auditors, built on **.NET 10** and the
**Microsoft Agent Framework (MAF)**. Given a date, a jurisdiction, and a set of topic groups, the
scanner runs an agentic Retrieval-Augmented-Generation (RAG) pipeline that searches authoritative
primary sources, evaluates each result for relevance, enriches and categorizes it, and persists a
versioned, audit-ready record of every regulatory update it finds.

> This is a public, MIT-licensed reference implementation. It ships with synthetic/public-domain
> sample data (UK payroll & employment-tax keyword lists + gov.uk source allowlists) and contains
> no customer-identifying material. Never commit real endpoints, keys, or connection strings.

---

## What it does

Auditors need to know **what regulatory updates were published in a given period** for a given
jurisdiction, scoped to the topics they care about. Doing this by hand across many government and
legislative sources is slow and error-prone. The Agentic RAG Scanner automates it:

- **Input:** a reference **date** + a **jurisdiction** (e.g. *United Kingdom*) + selected
  **topic groups** (dense OR-lists of keyword/synonym phrases such as *Payroll Withholding*,
  *Fringe Benefits*, *Expatriates*).
- **Output:** a set of evaluated, categorized, and summarized regulatory result items — one
  versioned document per item per run — ready for export (CSV/Excel) and audit.

The pipeline is **effective-date aware** (it distinguishes publication date vs. in-force date vs.
tax-year applicability), uses a **three-verdict relevance evaluation**
(`RELEVANT` / `BORDERLINE` / `NOT_RELEVANT`), and searches only an **allowlist of primary sources**
(e.g. `gov.uk/hm-revenue-customs`, `legislation.gov.uk`, `supremecourt.uk`).

---

## How it works

The orchestrator fans the request out into **one MAF workflow per topic group**. Each workflow runs
an iterative, history-aware agentic RAG loop under a shared throttle (to respect model TPM/RPM and
search QPS limits):

```
Auditor Request (date + jurisdiction + topic groups)
   └─ Fan-out by topic group → N MAF workflows (shared throttle)
        ── Agentic RAG loop (per group, default maxLoops = 3) ──
        1. Query Synthesis Agent     synthesize focused queries from the keyword set; uses in-memory
                                      search history to avoid redundancy, and the previous pass's
                                      reviewer steer (broaden for missing facets vs. deepen/pivot)
        2. Web Search Agent (Bing)   search restricted to the primary-source allowlist
        3. Deterministic Pre-filter  dedupe (incl. cross-group) + URL reachability
        4. Full-text Fetch & Clean   HTML/PDF, strip boilerplate; fallback + flag "unverified"
        5. Relevance Eval Agent       single full-text, date-aware LLM call →
                                      RELEVANT / BORDERLINE / NOT_RELEVANT, extracting publication /
                                      effective / applies-from-to dates and a loop-feedback steer
        6. Loop Controller            route per-item verdicts (vetted vs. discarded), snapshot each
                                      carried item's cleaned full text to blob for provenance; re-loop
                                      while under maxLoops if the goal is unmet or a pass is
                                      ≥80% RELEVANT (recall override — a rich vein implies more to find)
        ── Routing & enrichment ──
        7. Content Analysis / Enrichment   whatItDoes summary + metadata
        8. Categorize Agent                impact area, regulator, approved tags
        9. Summarize & Impact Agent        plain-English summary + effective date
   └─ Deterministic Quality Gates    schema validation, dedupe vs. store, level-of-authority
   └─ Result Docs → Cosmos DB        one versioned doc per item per run
```

The **Query Synthesis** and downstream agents are **MAF agents over a Microsoft Foundry model
deployment**. The **Web Search** agent is a **pre-provisioned Foundry agent** (created in the Foundry
portal with *Grounding with Bing Custom Search*), which the MAF workflow resolves by name and runs to
execute the synthesized queries. A per-run, in-memory
**search history** (`searchQueries[]`, `vettedResults[]`, `discardedResults[]`) feeds both query
synthesis (to avoid redundant queries) and evaluation (to assess coverage).

Each per-group workflow is built as a **seven-executor MAF graph** (Query Synthesis → Web Search →
Pre-filter → Fetch & Clean → Relevance Eval → Loop Controller → Finalize), where the Loop Controller
branches on a conditional edge — `Retry` loops back to Query Synthesis for another pass, `Finalize`
exits to the Finalize tail. This decomposition enables mid-pass checkpoint resume.

For full design details see [docs/horizon-scanner-architecture.md](docs/horizon-scanner-architecture.md),
[docs/architecture-context.md](docs/architecture-context.md), and
[docs/maf-executor-design.md](docs/maf-executor-design.md) (the executor decomposition).

---

## Solution structure

| Project | Purpose |
|---------|---------|
| `AgenticRagScannerApi` | ASP.NET Core Web API host — controller, orchestrator, services, DI wiring, configuration, and validation. |
| `AgenticRagScannerApi.Core` | Shared domain contracts (`TopicGroup`, `RunContext`, `ResultItem`, verdict/authority enums), runtime types, and the shared-throttle abstraction. |
| `AgenticRagScannerApi.Workflows` | The per-topic-group MAF workflow — the seven-executor agentic-RAG graph (with conditional loop-back), agents, steps, tools, prompts, and Cosmos-backed checkpointing. |
| `tests/AgenticRagScannerApi.Tests` | xUnit test project. |
| `docs/` | Architecture primers, implementation plan, and the epic/story backlog. |

---

## Required Azure services

| Service | Role | Status |
|---------|------|--------|
| **Microsoft Foundry** (+ model deployment) | Hosts the models behind every LLM call (query synthesis, relevance eval, categorize, summarize). | Required |
| **Grounding with Bing Custom Search** (Foundry connection) | Web search for the Web Search agent, scoped to the primary-source allowlist. | Required |
| **Azure Cosmos DB** | Versioned result documents (one per item per run) and MAF workflow checkpointing. | Required |
| **Azure Storage account** (Blob) | Storage for fetched documents, exports, and working artifacts. | Required |
| **Azure AI Search** | Memory/learnings store (planned/future feature #8). | Optional / Planned |
| **Application Insights** | Structured logging + telemetry sink (via Serilog). | Optional |

Authentication is **keyless by default** using `DefaultAzureCredential` (Managed Identity / developer
sign-in). API keys and connection strings are honored for **local development only** — assign your
identity the appropriate data-plane roles (e.g. *Storage Blob Data Contributor*,
*Cosmos DB Built-in Data Contributor*, *Search Index Data Contributor*, and the relevant Foundry /
Cognitive Services roles).

---

## Tech stack

- **.NET 10 (LTS)** / C#, ASP.NET Core Web API
- **Microsoft Agent Framework** (`Microsoft.Agents.AI`, `Microsoft.Agents.AI.Workflows`,
  `Microsoft.Agents.AI.Foundry`) for agent orchestration
- **Microsoft.Extensions.AI** + **Azure.AI.OpenAI** / **Azure.AI.Projects** for model access
- **Azure SDKs:** `Azure.Storage.Blobs`, `Azure.Search.Documents`, `Microsoft.Azure.Cosmos`,
  `Azure.Identity`
- **FluentValidation** for request validation, **Riok.Mapperly** for mapping,
  **Polly** for resilience
- **Serilog** (Console + Application Insights) for structured logging
- **Scalar** for OpenAPI/API documentation UI

---

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Azure Developer CLI (`azd`)](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd)
- An Azure subscription with the [required services](#required-azure-services) provisioned
- Azure CLI signed in (`az login`) so `DefaultAzureCredential` can authenticate locally

### Provision infrastructure with azd

The repository includes Bicep infrastructure in `infra/` and a post-provision hook configured in
`azure.yaml`.

When you run `azd up` or `azd provision`, the post-provision hook performs these actions:

* Assigns RBAC roles to the signed-in user for local development.
* Assigns RBAC roles to the Foundry account and Foundry project managed identities.
* Optionally assigns RBAC roles to an additional principal via `AZURE_RBAC_PRINCIPAL_ID`.
* Upserts Bing Custom Search configuration.
* Creates the Foundry project connection for Bing.
* Deploys or updates the Bing-grounded Foundry agent.

Runbook:

1. Authenticate once.

  ```powershell
  azd auth login
  ```

2. Create or select an environment.

  ```powershell
  azd env new <env-name>
  ```

3. Optional: set a stable base name for Azure resources.

  ```powershell
  azd env set AZURE_BASE_NAME <base-name>
  ```

4. If your tenant blocks Graph API lookups with Conditional Access, set your Entra object ID to
  avoid interactive principal resolution during RBAC setup.

  ```powershell
  azd env set AZURE_RBAC_PRINCIPAL_ID <user-object-id-guid>
  ```

5. Optional: control Bing agent deployment behavior.

  ```powershell
  azd env set DEPLOY_BING_AGENT_ON_PROVISION true
  azd env set FOUNDRY_BING_CONNECTION_NAME <connection-name>
  azd env set FOUNDRY_BING_INSTANCE_NAME <bing-instance-name>
  ```

6. Provision infrastructure and run post-provision automation.

  ```powershell
  azd up --no-prompt
  ```

7. For a clean reprovision cycle, tear down and provision again.

  ```powershell
  azd down --no-prompt
  azd up --no-prompt
  ```

> [!IMPORTANT]
> If your tenant enforces strict Conditional Access and `az ad signed-in-user show` returns
> `InteractionRequired` or `TokenCreatedWithOutdatedPolicies`, set
> `AZURE_RBAC_PRINCIPAL_ID` to your user object ID. This bypasses Graph object ID lookup in the
> RBAC tool and prevents repeated authentication prompts.

### Configure

1. Copy the example local settings file:

   ```powershell
   Copy-Item AgenticRagScannerApi/appsettings.Local.json.example AgenticRagScannerApi/appsettings.Local.json
   ```

2. Fill in your real endpoints in `appsettings.Local.json`. This file is **git-ignored** — never
   commit real endpoints, keys, or connection strings. Prefer leaving `ApiKey` / `ConnectionString`
   blank and relying on `DefaultAzureCredential`.

   Key configuration sections:

   - `Foundry` — Foundry endpoint + model deployment name (downstream MAF agents)
   - `WebSearch` — Foundry project endpoint + the name of the pre-provisioned Web Search
     agent (optionally a pinned `AgentVersion`), plus `MaxResults` and `RequestTimeoutSeconds`
   - `Cosmos` — account endpoint, database, and checkpoints container
   - `AzureStorage` — blob service URI + container names (`documents`, `exports`)
   - `AzureSearch` — search endpoint + index name (planned memory store)
   - `Fetch` — full-text fetch limits (allowed content types, max response size,
     max redirects, request timeout)
   - `ApplicationInsights` — connection string (optional)

### Build & run

```powershell
dotnet build AgenticRagScannerApi.sln
dotnet run --launch-profile https --project AgenticRagScannerApi/AgenticRagScannerApi.csproj
```

The API starts at `https://localhost:7022` and opens the Scalar API docs at
`https://localhost:7022/scalar/v1`. A `/health` endpoint is available for health checks.

VS Code tasks are also provided: `build`, `run-api`, `watch-api`, `run-tests`, and
`run-tests-with-coverage`.

### Infrastructure CLI tools

The `infra/tools` folder contains helper CLIs used by the `azd` post-provision workflow:

* `AgenticRagScanner.RbacCli` assigns ARM and service-specific RBAC roles.
* `AgenticRagScanner.BingCustomSearchCli` upserts Bing Custom Search configuration and Foundry
  connections.
* `AgenticRagScanner.DeployAgentCli` deploys or updates the Bing-grounded Foundry agent from YAML.

### Trigger a scan

```http
POST https://localhost:7022/api/v1/scanner/scan
Content-Type: application/json

{
  "asOfDate": "2026-04-06",
  "jurisdiction": "United Kingdom",
  "topicGroups": [ "Payroll Withholding", "Fringe Benefits" ]
}
```

The scan runs **synchronously** for the POC and returns the aggregated per-topic-group results
(`200`). There is no run-status polling in the current phase.

### Test

```powershell
dotnet test AgenticRagScannerApi.sln
```

---

## Project status

The solution is delivered in phased epics tracked in [docs/backlog.md](docs/backlog.md). Epics 0–6 are
complete — foundations & contracts, run lifecycle, MAF scaffolding, the first real Foundry agent, the
Web Search agent, full-text fetch & clean with blob storage, and the **date-aware, three-verdict
relevance evaluation with the real loop controller** (per-item verdict routing, full-text provenance
snapshots, a ≥80%-RELEVANT recall override, and a loop-feedback steer back into query synthesis).
The per-group loop has since been decomposed from a single self-looping executor into a
**seven-executor MAF graph** with mid-pass checkpoint resume — see
[docs/maf-executor-design.md](docs/maf-executor-design.md).
Later epics cover enrichment & categorization, quality gates + Cosmos persistence, publish/export,
and the future memory/review loop.

## License

[MIT](LICENSE)
