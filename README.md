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
        1. Query Synthesis Agent     synthesize focused queries from the keyword set;
                                      uses in-memory search history to avoid redundancy
        2. Web Search Agent (Bing)   search restricted to the primary-source allowlist
        3. Deterministic Pre-filter  dedupe (incl. cross-group) + URL reachability
        4. Full-text Fetch & Clean   HTML/PDF, strip boilerplate; fallback + flag "unverified"
        5. Relevance Eval Agent       single LLM call → RELEVANT / BORDERLINE / NOT_RELEVANT
        6. Loop Controller            re-loop if under maxLoops & goal unmet, or >80% RELEVANT
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

For full design details see [docs/horizon-scanner-architecture.md](docs/horizon-scanner-architecture.md)
and [docs/architecture-context.md](docs/architecture-context.md).

---

## Solution structure

| Project | Purpose |
|---------|---------|
| `AgenticRagScannerApi` | ASP.NET Core Web API host — controller, orchestrator, services, DI wiring, configuration, and validation. |
| `AgenticRagScannerApi.Core` | Shared domain contracts (`TopicGroup`, `RunContext`, `ResultItem`, verdict/authority enums), runtime types, and the shared-throttle abstraction. |
| `AgenticRagScannerApi.Workflows` | MAF workflow scaffolding — agents, steps, pipeline, tools, prompts, and Cosmos-backed checkpointing. |
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
- An Azure subscription with the [required services](#required-azure-services) provisioned
- Azure CLI signed in (`az login`) so `DefaultAzureCredential` can authenticate locally

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
     agent (optionally a pinned `AgentVersion`)
   - `Cosmos` — account endpoint, database, and checkpoints container
   - `AzureStorage` — blob service URI + container names (`documents`, `exports`)
   - `AzureSearch` — search endpoint + index name (planned memory store)
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

The solution is delivered in phased epics tracked in [docs/backlog.md](docs/backlog.md). Epics 0–4
(foundations & contracts, run lifecycle, MAF scaffolding, the first real Foundry agent, and the
Web Search agent) are complete; later epics cover full-text fetch, evaluation, enrichment,
persistence, and the future memory/review loop.

## License

[MIT](LICENSE)
