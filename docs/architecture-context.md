# Agentic RAG Scanner — Architecture & Context Primer

---

## 1. What this project is

- **Repo:** [ctjoumas/agentic-rag-scanner](https://github.com/ctjoumas/agentic-rag-scanner)
- **License:** MIT
- **Stack:** .NET 10 (LTS) / C#, Microsoft Agent Framework (MAF) for orchestration.
- **Local clone path:** `C:\Users\chtjouma\source\repos\agentic-rag-scanner`

> **Target framework decision:** **.NET 10 (LTS, released Nov 2025, supported to ~Nov 2028)**.
> Chosen over .NET 9 because .NET 9 is STS and reached end of support around May 2026 — not
> appropriate for a new, customer-facing reference repo. The SDK on the dev machine is .NET 10.

### Public-repo hygiene (important)
Because this is public, MIT-licensed, and mirrors the customer's setup with *their sample data*:
- No customer name, product name, or internal endpoints in code, comments, config, commit
  messages, or the README.
- No real connection strings / resource names — use placeholders in `.env.example` and Bicep params.
- Confirm sample data (topic groups, source allowlists, seed docs) is OK to publish, or swap in a
  clearly synthetic equivalent. UK payroll/employment-tax keyword lists + gov.uk source lists are
  public-domain regulatory info and are likely fine — confirm with the customer.

---

## 2. Domain & inputs

Regulatory scanning for auditors.

- **Input:** a **date** + **jurisdiction** (e.g., United Kingdom) + selected **topic groups**.
- **Topic groups** are dense **OR-lists of keyword/synonym phrases** (acronyms/aliases). Example
  UK payroll/employment-tax groups: Payroll Withholding, Employer Payroll Taxes, Payroll Reporting,
  Fringe Benefits, Retirement Benefits, Equity and Incentives, Expatriates, Miscellaneous.
- **Authoritative sources (allowlist)** — searches are restricted to primary sources, e.g.:
  - `https://www.gov.uk/government/organisations/hm-revenue-customs/`
  - `https://www.supremecourt.uk/`
  - `https://www.legislation.gov.uk`

Because topic groups are keyword OR-lists, **query synthesis must rotate synonym coverage across
passes** rather than firing a fixed set of queries.

---

## 3. Target architecture (the pipeline)

One **MAF workflow per topic group**, all running **in parallel**, sharing a throttle so they
respect Azure OpenAI TPM/RPM and Bing QPS limits.

```
1. Auditor Request (date + jurisdiction + topic groups)
2. Fan-out by Topic Group  → N parallel MAF workflows
3. MAF Workflow (per group, shared throttle)
   ── Agentic RAG Loop (Stage 1 merged with old Stage 1.5) ──
   4. Query Synthesis Agent      (focused query from keyword set; uses in-memory search
                                  history to avoid redundant queries)
   5. Bing Search (Azure)        (restricted to primary-source allowlist — gate at query time)
   6. Deterministic Pre-filter   (dedupe incl. cross-group; URL reachability/validity)
   7. Full-text Fetch & Clean    (HTML/PDF; strip boilerplate; fallback to Bing summary +
                                  flag "unverified" rather than dropping)
   9. Full-text Relevance Eval   (single LLM call; RELEVANT / BORDERLINE / NOT_RELEVANT;
                                  effective-date aware; applies retrieved learnings)
   10. Sufficiency / Loop Controller
       └─ re-loop if under per-group maxLoops (default 3) AND goal unmet,
          OR override if a pass returns >80% RELEVANT
   11. Verdict Routing (in-memory)
       ├─ RELEVANT  → enrichment
       ├─ BORDERLINE (flagged, still carried forward) → enrichment
       └─ NOT_RELEVANT → dropped (logged for audit)
   12. Content Analysis / Enrichment   (whatItDoes summary; enrich metadata)
   13. Categorize Agent (Stage 2)      (impact area; regulator; approved tags)
   14. Summarize & Impact Agent (St.3) (RAG over history; effective date; plain-English)
15. Deterministic Quality Gates  (schema validation; dedupe vs Cosmos; level-of-authority
                                  stamping: legislation > court ruling > HMRC guidance)
16. Result Docs — Cosmos DB      (one versioned doc per item per run)
17. Published Regulatory Update  (auto-published; CSV/Excel export)

── FUTURE (not in POC) ──
8.  Memory / Learnings store (PLANNED — leaning Azure AI Search)
18. Admin UI — Review of Past Runs
19. Structured Review Capture (verdict-correction + reason-code tags + freeform note)
20. Distillation Job (rolls reviews into curated guidance rules → Memory store #8)
```

### Key design decisions
- **Stage 1 + Stage 1.5 merged** into a single **full-text** relevance eval (not summary-based):
  in compliance, false negatives are costlier than false positives, so don't pre-prune on summaries.
- **Query Synthesis Agent**: number of queries is the agent's call (NOT hard-coded to 4). On
  re-runs it consults the in-memory search history to craft non-redundant queries that target
  untested synonyms/gaps.
- **In-memory search history** (per topic group, per run, **not persisted**): a JSON object with
  `searchQueries[]`, `vettedResults[]`, `discardedResults[]`, appended each pass. Feeds query
  synthesis (avoid redundancy) and eval (coverage). Distinct from the planned cross-run Memory
  store (#8).
- **Three-verdict eval** (RELEVANT / BORDERLINE / NOT_RELEVANT), fully automated, **no human gate**
  in the POC. BORDERLINE carried forward but flagged in the data structure; NOT_RELEVANT dropped+logged.
- **Loop exit:** per-group tunable `maxLoops` (default 3); eval agent judges goal-satisfaction;
  accuracy override re-loops if a pass is >80% RELEVANT.
- **Effective-date aware eval:** distinguishes **publication date** vs **effective/in-force date**
  vs **tax-year/period applicability**; compares to the requested window; uses dates as a *signal*,
  not a hard filter; carries dates forward. Output schema fields: `publicationDate`, `effectiveDate`,
  `appliesFrom`/`appliesTo`, `dateConfidence`.
- **Quality gates (#15)** are non-LLM: schema validation + dedupe vs Cosmos + level-of-authority stamping.
- **Result store (#16):** one versioned Cosmos doc per item per run.

---

## 4. Azure services

| Service | Role | Status |
|---------|------|--------|
| **Microsoft Foundry** | Hosts the models used for all LLM calls (query synthesis, eval, categorize, summarize). | Core |
| **Bing — Grounding with Bing Search** | Web search over the primary-source allowlist. | Core |
| **Bing — Grounding with Bing Custom Search** | Custom-scoped search grounding. | Core |
| **Azure Storage account** | Blob/file storage for fetched documents, exports, working artifacts. | Core |
| **Azure AI Search** | Leaning choice for the (FUTURE) memory/learnings store (#8). | Planned |
| **Azure Cosmos DB** | Versioned result docs (#16), one per item per run. | Core (data) |
| **Azure OpenAI / model routing** | Via Foundry; respect TPM/RPM with a shared throttle. | Core |

**Concurrency:** a shared throttle (e.g. `SemaphoreSlim`) across all parallel workflows to respect
OpenAI TPM/RPM and Bing QPS.

---

## 5. Scaffolding plan (current task)

**Scope decision for the 2-week sprint:** the customer's production setup uses an **Azure Function**
(timer/trigger to run the scan) **plus a Web API** (endpoints). For this sprint we will **simplify to
a Web API only** and **manually trigger** scans. The Function can be added later if needed.

**ONLY scaffold the project structure now** (no business logic yet). Requirements:

- **.NET 10 Web API** as the host (endpoints).
- **Separate service classes** in a services layer, one per external service, behind interfaces:
  - Azure Storage account service
  - Azure AI Search service
  - Microsoft Foundry service (LLM calls)
  - Bing grounding service — **Grounding with Bing Search**
  - Bing grounding service — **Grounding with Bing Custom Search**
- **Dependency injection** wired throughout (interfaces registered in DI; services injected into
  controllers/handlers).
- Typical layering: API (controllers/endpoints) → Services (interfaces + implementations) → models/config.
- Config via `appsettings.json` + `.env.example`/options pattern with **placeholders only** (no secrets).

**Explicitly deferred (do NOT build yet):** Azure Function host, Cosmos persistence logic, the MAF
workflow/agent implementations, the memory/learnings store, the FUTURE review/distillation loop, infra (Bicep).