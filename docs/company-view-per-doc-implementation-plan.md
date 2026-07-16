# Implementation Plan — Company View per vetted document (Option A)

**Branch:** `update-company-view-per-doc`
**Status:** Draft for review (no code changes yet — this document only)
**Author:** (you)
**Date:** 2026-07-15

---

## 1. Decision & background

The customer confirmed **Option A**: categorization and the Company View should be produced
**per vetted document (item)**, not once per topic group.

### How it works today (current state)

For each topic group, the finalize step runs **once at group level**:

1. Route verdicts → the set of *carried* (Relevant / Borderline) items.
2. Read each carried item's vetted full-text snapshot back from blob, and run per-item **enrichment**.
3. Run **Impact Area** (single-label) and **Tags** (multi-label) **once for the whole group**, each grounded on *all* the carried items' full text (run concurrently).
4. Run **Company View** **once for the whole group**, producing a single `CompanyViewRecord` that aggregates every carried item, grounded on all items' full text + the group-level impact area/tags, steered by prior Company Views (RAG by jurisdiction).
5. Emit one `TopicGroupResult` whose `CompanyView` holds that single aggregate record.

Key facts that shape this change:

- `ResultItem` **already has** `ImpactArea` and `Tags` properties, but **nothing populates them** today — they always serialize as `null` / `[]`.
- The group-level record lives on `TopicGroupResult.CompanyView`.
- Results are **not persisted to Cosmos** — `TopicGroupResult` is serialized directly into the HTTP response. Only full text is persisted (to blob) and MAF checkpoints (to Cosmos `checkpoints`). So this change is **response-shape + workflow logic only**; there is no results-store migration.

### Relevant files (current)

| Concern | File |
| --- | --- |
| Item model | `AgenticRagScannerApi.Core/Contracts/ResultItem.cs` |
| Company View record | `AgenticRagScannerApi.Core/Contracts/CompanyViewRecord.cs` |
| Group result model | `AgenticRagScannerApi.Core/Runtime/TopicGroupResult.cs` |
| Finalize orchestration | `AgenticRagScannerApi.Workflows/Executors/FinalizeExecutor.cs` |
| Impact Area agent | `AgenticRagScannerApi.Workflows/Agents/{IImpactAreaAgent,ImpactAreaAgent,ImpactAreaAgentStub}.cs` |
| Tags agent | `AgenticRagScannerApi.Workflows/Agents/{ITagsAgent,TagsAgent,TagsAgentStub}.cs` |
| Company View agent | `AgenticRagScannerApi.Workflows/Agents/{ICompanyViewAgent,CompanyViewAgent,CompanyViewAgentStub}.cs` |
| Prompts | `AgenticRagScannerApi.Workflows/Prompts/{ImpactAreaPrompt,TagsPrompt,CompanyViewPrompt,AggregateContextBuilder}.cs` |
| API result shape | `AgenticRagScannerApi/Models/ScanResult.cs` |
| Prior views (RAG) | `AgenticRagScannerApi.Workflows/CompanyView/IPriorCompanyViewSource.cs`, `AgenticRagScannerApi/Services/CsvPriorCompanyViewSource.cs` |

---

## 2. Target design

Move Impact Area, Tags, and Company View from **group-level** to **per-item**:

- Each carried `ResultItem` gets a **new** `companyView` record, produced **only from that item's own
  full text** (plus prior-view exemplars for house style, which stay per-jurisdiction and unchanged).
- **Impact area and tags are NOT separate item fields.** They live **inside** each item's
  `companyView` record (`CompanyViewRecord` already carries `ImpactArea` and `Tags`). The existing
  unused `ResultItem.ImpactArea` and `ResultItem.Tags` properties are **removed**.
- The group-level `TopicGroupResult.CompanyView` is **removed**. The output no longer has a separate
  `companyView` object per group; instead every item carries its own.

### Output shape change (illustrative)

Before (per group):

```jsonc
{
  "groupId": "employee-nic-b726f89f",
  "items": [ { "id": "…", "impactArea": null, "tags": [] }, … ],
  "companyView": { "impactArea": "…", "tags": ["…"], "companyView": "…", … }
}
```

After (per item):

```jsonc
{
  "groupId": "employee-nic-b726f89f",
  "items": [
    {
      "id": "…",
      // no top-level impactArea / tags on the item — they live inside companyView
      "companyView": {
        "impactArea": "Employment taxes rates & thresholds",
        "tags": ["National Insurance"],
        "titleOfUpdate": "…",
        "summaryOfUpdate": "…",
        "companyView": "…",
        "levelOfAuthority": "…",
        "statusOfChange": "…",
        "announcementDate": "…",
        "effectiveDateOfChange": "…",
        "supportingReference": "…",
        "regulator": "…"
      }
    }
  ]
  // no group-level companyView
}
```

### Decision: impactArea/tags live only inside the item's Company View

**Decided (A2).** `CompanyViewRecord` already contains `ImpactArea` and `Tags`, so those are the single
source of truth per document. The plan therefore **removes** `ResultItem.ImpactArea` and
`ResultItem.Tags` (both currently unused — verified: no code writes or reads them) rather than
duplicating the values on the item body. Each item's categorisation is read from `item.companyView`.

---

## 3. Detailed changes by area

### 3.1 Contracts / models

- **`ResultItem.cs`**:
  - **Add** the per-item record:
    ```csharp
    /// <summary>Per-item Company View record (Option A: one per vetted document). Carries this
    /// document's impact area, tags, summary, and practitioner view.</summary>
    public CompanyViewRecord? CompanyView { get; set; }
    ```
  - **Remove** the now-redundant `ImpactArea` and `Tags` properties (impact area + tags live inside
    `CompanyView`). Verified no writers/readers exist, so removal is safe.
  - **Remove `Regulator` too.** It is an unpopulated placeholder — the only enrichment implementation is
    `EnrichmentAgentStub`, which sets only `WhatItDoes`; no code ever assigns `ResultItem.Regulator`
    (confirmed by output showing `"regulator": null`). The real regulator value is synthesized into
    `CompanyViewRecord.Regulator` by the Company View agent, so it lives only there now.
  - Leave `WhatItDoes` (populated by the enrichment stub) and `LevelOfAuthority` (quality-gate enum,
    distinct type/semantics from the record's free-text field) as-is — out of scope.
- **`TopicGroupResult.cs`** — remove the `CompanyView` property and update its XML doc.
- **`CompanyViewRecord.cs`** — no structural change; update the class summary to say it now represents a
  **single document's** record (not a group aggregate). The CSV-column mapping is unchanged.

### 3.2 Agent interfaces + implementations (group → single item)

Change all three from a list-based, group-wide signature to a **single-item** signature.

- **`IImpactAreaAgent` / `ImpactAreaAgent`**
  ```csharp
  Task<string?> SelectAsync(ResultItem item, string? fullText, TopicGroupContext context, CancellationToken ct = default);
  ```
- **`ITagsAgent` / `TagsAgent`**
  ```csharp
  Task<IReadOnlyList<string>> SelectAsync(ResultItem item, string? fullText, TopicGroupContext context, CancellationToken ct = default);
  ```
- **`ICompanyViewAgent` / `CompanyViewAgent`**
  ```csharp
  Task<CompanyViewRecord?> GenerateAsync(ResultItem item, string? fullText, string? impactArea, IReadOnlyList<string> tags, IReadOnlyList<CompanyViewRecord> priorViews, TopicGroupContext context, CancellationToken ct = default);
  ```
  - `BuildBaseRecord` becomes single-item: `AnnouncementDate` / `EffectiveDateOfChange` from that item's
    dates; `SupportingReference` from that item's `SourceUrls`.
  - **Prior-view exemplars are now passed IN** (`priorViews`) rather than fetched inside the agent — see
    §3.4a. The agent no longer depends on `IPriorCompanyViewSource`; it just applies the exemplars it's
    given (still capped by `MaxExemplars`, applied once by the caller).
  - Failure/empty behaviour preserved (return record with objective fields only; return `null` only when
    there is genuinely no item — which won't happen in a per-item loop, so revisit the null contract).

### 3.3 Prompts

- **`AggregateContextBuilder`** — currently builds an "N updates for this group" block. Add / switch to a
  **single-document** block (header wording like *"Regulatory update (full text)"* instead of
  *"consider ALL of them"*). Simplest: keep the method but call it with a one-item list, then adjust the
  header copy so it doesn't imply multiple documents.
- **`CompanyViewPrompt`** — reword system + user prompts from *"aggregate them into ONE record — do not
  write a separate entry per update"* to *"produce the Company View for THIS single regulatory
  document."* **Bump `Version`** (v5 → v6).
- **`ImpactAreaPrompt` / `TagsPrompt`** — reword "for this topic group as a whole" → "for this
  regulatory document." **Bump both `Version`s** (v2 → v3) for eval traceability.

### 3.4 Finalize orchestration — `FinalizeExecutor`

Replace the group-level block (steps 3–4 above) with a **per-item loop**:

```
// hoisted once per group (see §3.4a):
priorViews = (await priorViewSource.GetByJurisdictionAsync(ctx.Run.Jurisdiction)).Take(MaxExemplars)

for each carried item:
    fullText = fullTextByItemId[item.Id]
    (impactArea, tags) = await WhenAll(impactAreaAgent.SelectAsync(item, fullText, ctx),
                                       tagsAgent.SelectAsync(item, fullText, ctx))
    // impactArea + tags are stamped INTO the record by GenerateAsync (BuildBaseRecord),
    // not onto the item body (A2).
    item.CompanyView = await companyViewAgent.GenerateAsync(item, fullText, impactArea, tags, priorViews, ctx)
```

- Build `TopicGroupResult` **without** `CompanyView`.
- Update the executor's XML `<summary>` to describe per-item categorisation.

**Design decision — keep 3 separate agents per item (DECIDED).** Impact area, tags, and company view stay
as three separate agents/prompts, run per item. Rationale: (1) impact area + tags must be validated
against the customer's controlled Cosmos vocabulary — the separate agents do deterministic `Normalize`
(off-list impact area → `null`; off-list tags dropped), a compliance-grade correctness guarantee a single
blended prompt would lose; (2) independent prompt versioning + evaluation per facet (regressions stay
attributable); (3) failure isolation (a degraded tags call still yields impact area + company view);
(4) constrained classification and open generative advice in one prompt tend to degrade each other. The
cost of the split is mitigated by hoisting shared work (§3.4a) and the already-cached vocabulary fetch —
not by collapsing prompts. Within an item the calls form **2 waves** (impact area + tags concurrently,
then company view). Items are processed **sequentially** for now; per-item parallelisation is deferred to
Phase 13 (see §3.4b).

### 3.4a Hoist the prior-view fetch out of the per-item loop

All items in a group share one jurisdiction, so the prior Company View exemplars must be fetched **once**
per group, not per item:

- Move the `IPriorCompanyViewSource.GetByJurisdictionAsync(...)` call (and the `Take(MaxExemplars)` cap)
  **into `FinalizeExecutor`**, before the per-item loop; pass the resulting `priorViews` list into each
  `GenerateAsync` call.
- **Remove** `IPriorCompanyViewSource` (and `MaxExemplars` handling) from `CompanyViewAgent`'s
  constructor/body — the agent becomes purely "given item + full text + impactArea/tags + exemplars,
  produce the record." `FinalizeExecutor` gains the `IPriorCompanyViewSource` dependency instead.
- Note: our CSV-backed source already caches by jurisdiction in-memory, so in *this* codebase the
  per-item call is cheap — but that caching is only our POC implementation. In the customer's environment
  the prior views come from a **data lake** fetch (they will replace our CSV caching code with data-lake
  retrieval), where a per-item call *would* be a real, repeated network cost. Fetching once per group is
  therefore the correct design regardless, and it keeps the agent single-responsibility.

**Cost / latency / throttling:** per-item work multiplies LLM calls by the carried item-count, so all
per-item LLM calls must route through the existing shared throttle in
`AgenticRagScannerApi.Core/Throttling/*` to stay within model TPM/RPM limits. Impact area + tags stay
concurrent within an item (they're independent); see §3.4b for item-level ordering.

### 3.4b Per-item processing order & empty-group behaviour (DECIDED)

- **Sequential for now.** Carried items are processed **one at a time** (the `for each carried item` loop
  runs sequentially). Parallelising the per-item fan-out is **deferred to Phase 13 (Fan-out &
  parallelization)** — the same phase that parallelises topic-group execution under the shared throttle.
  When Phase 13 lands, this loop becomes a throttle-gated `Task.WhenAll` over items. (Impact area + tags
  already run concurrently within an item — that stays.) See
  `docs/implementation-plan.md` § Phase 13 and `docs/0.4-shared-throttle.md`.
- **Empty group (zero carried items).** Nothing to categorise, so:
  - **no `CompanyView` is produced anywhere** (there are no items to attach one to);
  - `TopicGroupResult.Items` is empty;
  - the group's `History` snapshot conveys the outcome — the final pass's review makes clear that no
    relevant items were found, and `Status` is `Completed` for a clean empty scan (or `Failed` when the
    final web search itself failed, as today).
  Confirm the history message reads clearly for the "searched, found nothing" case.

### 3.5 Stubs (needed for offline workflow tests)

Update `ImpactAreaAgentStub`, `TagsAgentStub`, `CompanyViewAgentStub` to the new single-item signatures
and return canned per-item values.

### 3.6 API / serialization

- No mapper change needed — `ScanMapper` only maps request → response envelope; the result body is the
  `ScanResult` → `TopicGroupResult` graph serialized directly.
- The response JSON shape changes (companyView moves under each item; group companyView removed). Update
  any example payloads / docs (`AgenticRagScannerApi.http`, architecture docs) that show the old shape.

### 3.7 Tests to update

| Test | Change |
| --- | --- |
| `CompanyViewAgentTests` | New single-item signature; assert per-item record; dates/refs from the one item. |
| `TopicGroupWorkflowTests` | `result.CompanyView` assertions → assert `result.Items[i].CompanyView`. |
| `WorkflowResumeTests`, `WorkflowTestFactory` | Update to per-item result assertions / stub wiring. |
| `ScanOrchestratorTests`, `ScannerControllerTests` | `TopicGroupResult` construction no longer sets `CompanyView`. |
| (Impact Area / Tags agent tests, if any) | New single-item signatures. |

---

## 4. Out of scope

- **Option B** (track which sub-topic each document covers, then Company View per sub-topic) — explicitly
  not chosen by the customer.
- Persisting results to a Cosmos `results` container — results are still returned in-response only.
- `NU1903` `Microsoft.OpenApi` 2.0.0 vulnerability bump — separate follow-up branch.

---

## 5. Validation plan

1. `dotnet build AgenticRagScannerApi.sln` — 0 errors (pre-existing NU1903 warnings expected).
2. `dotnet test AgenticRagScannerApi.sln` — all tests green after updates.
3. Manual smoke: POST a scan request (VPN → Cosmos/Storage connected), confirm each item carries its own
   `impactArea`, `tags`, and `companyView`, and that no group-level `companyView` remains.
4. Sanity-check prompt version bumps show up in logs/eval traces.

---

## 6. Implementation chunks (green at every step)

The change is cross-cutting (contracts → agents → executor → tests), so an **additive-then-subtractive**
order keeps the build and all tests green at every review point. Three self-contained, reviewable chunks:

**Chunk 1 — Additive scaffolding (no behaviour change).**
- Add `ResultItem.CompanyView` (nullable; unused for now).
- Add the new single-item methods **alongside** the existing group methods on the three agents
  (`SelectAsync(item, fullText, …)`, `GenerateAsync(item, …, priorViews, …)`) and implement them, leaving
  the old group methods in place and still wired.
- Add the single-item variants to the stubs.
- Add unit tests for the new single-item methods.
- **Green:** build + all tests pass; runtime output unchanged.

**Chunk 2 — Wire the finalize loop to per-item (behaviour switch).**
- Rewrite `FinalizeExecutor`: hoist the prior-view fetch (move `IPriorCompanyViewSource` here), loop
  carried items **sequentially**, populate `item.CompanyView`; stop producing the group-level record.
- Update workflow tests (`TopicGroupWorkflowTests`, `WorkflowResumeTests`/`WorkflowTestFactory`) to assert
  per-item `CompanyView` and the empty-group behaviour (§3.4b).
- **Green:** build + tests pass; output is now per-item. `TopicGroupResult.CompanyView` still exists on the
  model but is left unset (removed in Chunk 3).

**Chunk 3 — Subtractive cleanup + prompt rewrites.**
- Remove `TopicGroupResult.CompanyView`; remove `ResultItem.ImpactArea`, `Tags`, and `Regulator`.
- Remove the old group methods from the three agents + stubs; remove `IPriorCompanyViewSource` from
  `CompanyViewAgent`.
- Reword the three prompts to single-document and bump versions (`CompanyViewPrompt` v5→v6,
  `ImpactAreaPrompt`/`TagsPrompt` v2→v3).
- Update remaining tests (`ScanOrchestratorTests`, `ScannerControllerTests`, `CompanyViewAgentTests`) and
  example payloads/docs (`AgenticRagScannerApi.http`).
- **Green:** build + tests pass; final shape delivered.

Each chunk is a compiling, tested commit you can review before the next begins.
