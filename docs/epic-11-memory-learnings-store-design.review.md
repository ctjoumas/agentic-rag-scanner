# Epic 11 Design — Review Comments & Suggested Edits

> Review of `docs/epic-11-memory-learnings-store-design.md` (branch `feature/phase-11-docs`).
> Callout colors: 🟥 **CAUTION** = blocker / must-change · 🟨 **WARNING** = change recommended ·
> 🟦 **NOTE** = confirmed decision to fold into the doc · 🟩 **TIP** = optional/later.
> These are inline comments keyed to the doc's section headings — apply as edits or paste into the PR review.

---

## §1 — Intent & the key decision

> [!CAUTION]
> **Reviewer — reviewer feedback is a HARD dependency, not an aside.**
> The epic's value is a human-in-the-loop *correction* signal. Seeding learnings from the pipeline's
> own prior completed answers (with no reviewer correction) is an **echo chamber**: it reinforces
> existing style and *compounds* mistakes rather than improving them. We have decided the **feedback
> form is a blocking prerequisite** for Epic 11 — until an approved feedback signal exists, the
> learnings store must **not** be relied upon or enabled. Please reframe §1 around this and move the
> feedback-capture dependency out of §8 "open questions" and into an explicit prerequisite.

> [!NOTE]
> **Reviewer — narrow the "three channels" claim.** The stated requirement is *better Company Views*,
> which is the **Finalize channel only**. Query Synthesis and Relevance Eval are indirect and are being
> **deferred** (see §3.3). Please reword §1 so Finalize/Company View is the primary (initial) channel
> and the other two are explicitly phased/optional.

**Suggested edit — replace the "three channels" list in §1 with:**

```markdown
Learnings reach the **final answer primarily through the Company View / Finalize channel**. Two further
channels are **deferred** and gated on a proven, approved corrective feedback signal (see §3.3):

1. **Company View / Finalize (the final answer itself) — PRIMARY, ships first.** The agent that writes
   each per-doc Company View is steered by prior **approved** completed answers for the same
   `jurisdiction + topic group`, via the existing `IPriorCompanyViewSource` seam.
2. **Query Synthesis (deferred) — validate/test only *after* the Finalize channel is in place.**
3. **Relevance Eval (deferred, possibly never) — see §3.3 for the correctness risk.**

Epic 11 depends on the **reviewer feedback form + approval workflow** (§3.6). Learnings are seeded
**only from reviewer-approved feedback**, never from unreviewed prior outputs — otherwise the store is a
self-reinforcing echo chamber and must stay disabled.
```

---

## §2 — Where it plugs into the pipeline

> [!WARNING]
> **Reviewer — the diagram/prose show QS + RE injection as first-class read paths.** Mark them
> **deferred** so the initial cut is unambiguous: the only READ path that ships first is
> `IPriorCompanyViewSource` → Finalize. Update the "Read path (new)" bullet accordingly and annotate the
> QS/RE "inject distilled learnings" arrows as *(Phase 2, deferred)*.

---

## §3.3 — Retrieval (READ) → inject into agents

> [!CAUTION]
> **Reviewer — defer Relevance Eval injection (possibly permanently).** "Steadier, prior-consistent
> verdicts" conflates *consistency* with *correctness*. A standing rule like the doc's own example
> ("treat gov.uk policy papers as BORDERLINE") will systematically mis-judge a policy paper that
> genuinely **is** in-force this time — a correctness regression baked into memory. Do **not** ship RE
> injection in Epic 11. If it is ever revisited, feed it only **structured** signals (authoritative-source
> lists, known false-positive hosts), never free-form guidance, and never as a hard rule.

> [!WARNING]
> **Reviewer — Query Synthesis injection is Phase 2, sequenced after Finalize.** Lower risk and
> plausibly useful, but it is **not** the requirement. Ship and validate the Finalize/Company View
> channel first, then trial QS injection behind its own flag and A/B it before adopting.

**Suggested edit — split the flags and mark deferral in §3.5 `LearningsOptions` (see below).**

---

## §3.4 — Final-answer steering (Company View)

> [!NOTE]
> **Reviewer — scope exemplars to `jurisdiction + topic group`, not jurisdiction alone.** Decision:
> lean to **jurisdiction + topic group** so exemplars are topically relevant (a payroll-withholding view
> shouldn't be steered by an unrelated group's exemplars). Flag for the customer that this trades a
> little breadth of "house style" for relevance — they should confirm, but our default is
> `jurisdiction + topic group`.

> [!WARNING]
> **Reviewer — the existing seam is jurisdiction-only.** `IPriorCompanyViewSource.GetByJurisdictionAsync(jurisdiction)`
> can't express the `+ topic group` scope above. Either extend the seam (e.g.
> `GetByJurisdictionAndGroupAsync(jurisdiction, groupId)`) or have the Cosmos-backed source take the
> group from context. Please note the seam change in §3.4 — it's no longer a pure "DI swap, no code change."

**Suggested edit — append to the `CosmosPriorCompanyViewSource` row / note in §3.4:**

```markdown
Exemplars are scoped to **`jurisdiction + topic group`** (not jurisdiction alone) so they are topically
relevant, and are restricted to **reviewer-approved** completed answers only. This requires extending
`IPriorCompanyViewSource` to accept the topic group (e.g. `GetByJurisdictionAndGroupAsync`), so this
channel is a small seam change, not only a DI swap.
```

---

## §3.5 — Configuration

> [!NOTE]
> **Reviewer — `LearningsOptions` should encode the decisions above.** Separate flags per channel,
> approval-gated retrieval, and top-K (default 5) as the *only* recency control (keep everything, pull
> the top-K most recent **approved**).

**Suggested edit — replace the `LearningsOptions` row:**

```markdown
| `LearningsOptions` | `Enabled` (master), `CompanyViewEnabled` (default true), `QuerySynthesisEnabled`
(default false, Phase 2), `RelevanceEvalEnabled` (default false — deferred), `TopK` (default 5),
`ExemplarScope` (`JurisdictionAndGroup` default \| `Jurisdiction`), `Distill` (on/off), `RequireApproved`
(default true). `ValidateDataAnnotations()` + `ValidateOnStart()`. |
```

---

## §3.6 (NEW) — Reviewer feedback capture & approval (prerequisite)

> [!CAUTION]
> **Reviewer — add this section; it's the heart of the epic.** Needed before any learnings are used:
>
> - **Capture form** shown after a Company View is generated: a **minimal structured signal**
>   (approve/reject or 1–5) **plus** free-form corrective text ("what's wrong / should change"). Keep
>   both — free text carries the richness, the structured bit makes it filterable/weightable.
> - **Approval workflow (admin section):** someone reviews submitted feedback and **approves** it.
>   **Only approved feedback/answers are eligible to be retrieved as learnings** (`RequireApproved`).
> - **Persisted fields** on the learning document: `reviewStatus` (`Pending`/`Approved`/`Rejected`),
>   `reviewer`, `reviewedAtUtc`, `rating`, `reasonCodes[]`, `feedbackText`. Retrieval filters
>   `reviewStatus = 'Approved'`.

---

## §4 — Data model

> [!WARNING]
> **Reviewer — `/jurisdiction` partition key + "keep everything" (`ttl: -1`) = a hot, unbounded partition.**
> If the workload is UK-heavy this is effectively a *single* logical partition holding every completed
> answer forever, plus audit/history rows — heading toward the 20 GB logical-partition cap and
> concentrated RUs. Since retrieval is now `jurisdiction + groupId`, prefer a **composite/synthetic
> partition key** (e.g. `jurisdiction|groupId`). Keep-all is fine for the *learnings* signal via top-K,
> but the partition key must scale.

> [!NOTE]
> **Reviewer — add the review/approval + feedback fields to the sample document** (`reviewStatus`,
> `reviewer`, `reviewedAtUtc`, `rating`, `reasonCodes`, `feedbackText`), and note that
> retrieval selects `WHERE ... AND reviewStatus = 'Approved'`.

> [!TIP]
> **Reviewer — dedup note.** With keep-all, re-scanning the same `jurisdiction + groupId + dateRange`
> creates near-duplicate rows that could dominate top-K. Approval gating mitigates this; optionally
> upsert on `(jurisdiction, groupId, dateRange)` among approved rows.

---

## §6 — Retrieval semantics

> [!NOTE]
> **Reviewer — be explicit: retrieval is top-K only (default 5), no "pull all".** State plainly that
> the store **keeps all** approved learnings but a query **only ever injects the top-K most recent
> approved** ones (`ORDER BY completedAtUtc DESC LIMIT @TopK`, default 5). Drop any "pull ALL" phrasing —
> "all" can't fit in a prompt and isn't intended.

**Suggested edit — replace the §6 "Ranking" bullet with:**

```markdown
- **Ranking:** **recency-ordered top-K only** (`ORDER BY completedAtUtc DESC`, `LIMIT TopK`, default 5),
  filtered to `reviewStatus = 'Approved'`. The store retains **all** approved learnings; a query never
  injects more than the top-K most recent. No `RecencyWindowDays` delete/TTL is required — top-K is the
  sole recency control.
```

> [!NOTE]
> **Reviewer — §6.1 reasoning is sound and can stay**, but update it so only the **Company View** bullet
> is active in the first release and the QS/RE bullets are labelled *(deferred)*, consistent with §1/§3.3.

---

## §7 — Mapping to backlog stories

> [!NOTE]
> **Reviewer — re-map to reflect phasing.** Story order should be: (1) reviewer feedback form + approval
> (prerequisite), (2) persist approved completed answers to Cosmos, (3) **Company View / Finalize**
> steering (the requirement), (4) *deferred* — Query Synthesis injection (validate after #3),
> (5) *deferred/possibly-never* — Relevance Eval injection. The "feed synthesis + eval" story (11.2)
> should be split so eval is not bundled with synthesis.

---

## §8 — Risks & open questions

> [!NOTE]
> **Reviewer — resolve these per the decisions above:**
>
> - **Reviewer-feedback capture:** RESOLVED — it is a **hard prerequisite**, not out of scope. Remove
>   the "confirm scope" hedge.
> - **Learning eligibility:** RESOLVED — **only reviewer-approved** feedback/answers are retrieved.
> - **Exemplar scope:** RESOLVED (our lean) — **`jurisdiction + topic group`**; flag for customer sign-off.
> - **QS / RE injection:** RESOLVED — QS deferred (after Finalize), RE deferred/possibly never.
> - **Recency:** RESOLVED — keep all approved, inject **top-K (default 5)** most recent; no TTL delete.
> - **Partition key (NEW):** OPEN — reconsider `/jurisdiction` vs composite `jurisdiction|groupId` for
>   scale (see §4).
> - **One container vs two:** one container + `docType` is fine to start (agreed).
