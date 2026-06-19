# Prompt management convention

> Story 3.2. Companion to `docs/implementation-plan.md` (AI-specific guardrails) and `docs/backlog.md` (Epic 3).

This repo keeps agent prompts **externalized and versioned** as C# prompt classes — not scattered
inline in agent code. This is the single convention for the solution; `.prompty` files are an optional
alternative format we do **not** use here.

## Where prompts live

- One static class per agent under `AgenticRagScannerApi.Workflows/Prompts/`, named `<Agent>Prompt.cs`
  (e.g. [`QuerySynthesisPrompt.cs`](../AgenticRagScannerApi.Workflows/Prompts/QuerySynthesisPrompt.cs)).
- The class exposes typed `Build…Prompt(...)` methods that compose the prompt via string interpolation.

## Required shape

Every prompt class:

1. Declares a `public const string Version` — a short tag (`"v1"`, `"v2"`, …). It starts at `"v0-stub"`
   while the agent is a stub and is bumped to `"v1"` when the real instructions land.
2. Exposes a `BuildSystemPrompt(...)` method — the agent's role, rules, and the **exact JSON response
   shape** it must return.
3. Optionally exposes a `BuildUserPrompt(...)` method — the per-pass data (topic group, keywords,
   prior queries, dates, etc.). Keeping it here means *all* prompt text is externalized and versioned.

```csharp
public static class QuerySynthesisPrompt
{
	public const string Version = "v1";

	public static string BuildSystemPrompt(string jurisdiction, int maxQueries) => $$"""...""";

	public static string BuildUserPrompt(TopicGroupContext context) { ... }
}
```

> Use a `$$"""…"""` raw string when the prompt contains literal `{` / `}` (e.g. a JSON example):
> interpolation holes become `{{placeholder}}` and literal braces stay single (`{`/`}`).

## Versioning rule

**Bump `Version` whenever the prompt instructions change.** Agents log the active `Version` on every
call, so eval runs (Epic 11) can attribute output/quality changes to a specific prompt revision.
Treat a `Version` bump like an API change: it is the unit of prompt history.

## Structured output

Prompts instruct the model to return **JSON only**, matching a fixed shape. Agents request a JSON
response format, validate the result against that shape, and apply a **bounded retry on invalid JSON**
before failing the item (never the whole run). See `QuerySynthesisAgent` for the reference pattern.

## Determinism

Document the temperature choice per agent. Synthesis uses a moderate temperature (to rotate synonym
coverage across passes); evaluation/categorization will use a low temperature for repeatability.
