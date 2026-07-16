# Per-Agent Model Deployments (Shared Foundry Project) — Implementation Plan

## Goal

Make each of the five in-process MAF agents configurable to use its **own Foundry model
deployment**, while all agents continue to share the **same Foundry endpoint/project**. This is an
**app-side configuration change only** — no infrastructure (Bicep) changes.

## Decisions (confirmed)

- **Scope:** Only the five in-process MAF agents get per-agent model configuration:
  - `QuerySynthesisAgent`
  - `RelevanceEvalAgent`
  - `ImpactAreaAgent`
  - `TagsAgent`
  - `CompanyViewAgent`
  - (`EnrichmentAgentStub` is a parked stub and makes no LLM calls — excluded.)
- The **Web Search agent** stays deploy-time configured. It is a *hosted* Foundry agent whose model
  is set at deploy time via the DeployAgent CLI / YAML (`--model` / `FOUNDRY_MODEL`) — not in scope.
- **Config shape:** Nested map under `Foundry`:
  `Foundry:Agents:<AgentName>:ModelDeploymentName`, falling back to the top-level
  `Foundry:ModelDeploymentName` when an agent override is absent or blank.
- **Infra:** No `foundry.bicep` changes. Model deployments referenced by config must be provisioned
  separately in the same Foundry account.
- `IFoundryService` (the non-agent facade) keeps using the default model deployment.

## Current architecture (facts)

- All five agents inject a **singleton `IChatClient`**, built in
  [ServiceCollectionExtensions.cs](../AgenticRagScannerApi/Extensions/ServiceCollectionExtensions.cs)
  in `AddFoundryChatClient` (around line 139).
- The client construction chain is:
  `AzureOpenAIClient.GetChatClient(modelDeploymentName).AsIChatClient()`
  → `ResilientChatClient` (shared throttle + Polly, configured from `FoundryOptions`)
  → `ChatClientBuilder.UseOpenTelemetry()`.
- Agents are registered in `AddWorkflowServices` (around line 183) as `AddSingleton<IX, X>()`.
- Agent constructors take a plain `IChatClient` — kept unchanged so existing unit tests (which inject
  a fake `IChatClient` directly) continue to compile and pass.
- `FoundryOptions` lives at
  [FoundryOptions.cs](../AgenticRagScannerApi/Configuration/FoundryOptions.cs).

## Approach

Introduce a small `IChatClientFactory` that builds and **caches a wrapped `IChatClient` per model
deployment name** (reusing a single shared `AzureOpenAIClient`, the `ResilientChatClient` wrapper, and
OpenTelemetry). Register each agent with an explicit factory lambda that resolves its per-agent model.
This keeps the agent classes untouched and makes model selection explicit at the DI registration site.

## Steps

1. **Config model** — In `FoundryOptions.cs`:
   - Add a `FoundryAgentOptions` type with a nullable `ModelDeploymentName`.
   - Add `Dictionary<string, FoundryAgentOptions> Agents { get; set; } = new();`.
   - Add a `ResolveModel(string agentKey)` helper returning
     `Agents[key].ModelDeploymentName` when present/non-blank, else the top-level `ModelDeploymentName`.

2. **Agent keys** — Add a `FoundryAgentKeys` constants class (QuerySynthesis, RelevanceEval,
   ImpactArea, Tags, CompanyView) so config keys and DI registrations stay in sync.

3. **Factory** — Add `IChatClientFactory` + `ChatClientFactory` under
   `AgenticRagScannerApi/Services/`. It builds the `AzureOpenAIClient` once (keyless via
   `DefaultAzureCredential`, or API key for local dev — same logic as today). `Create(modelDeploymentName)`
   returns a cached, `ResilientChatClient`-wrapped, OpenTelemetry-instrumented `IChatClient`.

4. **Refactor DI** — In `AddFoundryChatClient`, register the factory and make the default
   `IChatClient` resolve to `factory.Create(FoundryOptions.ModelDeploymentName)` (preserves
   `IFoundryService` and any other default consumers).

5. **Per-agent registration** — In `AddWorkflowServices`, register each of the five agents via a
   lambda that passes `factory.Create(options.ResolveModel(<key>))` into the constructor.

6. **Sample config** — Add an empty/example `Agents` block under `Foundry` in
   [appsettings.json](../AgenticRagScannerApi/appsettings.json) and
   `appsettings.Local.json.example`.

7. **Docs** — Update the Foundry configuration section in [README.md](../README.md) to describe the
   per-agent override map.

## Config example

```jsonc
"Foundry": {
  "Endpoint": "https://<resource>.openai.azure.com/",
  "ModelDeploymentName": "gpt-5-4",        // default / fallback for all agents
  "ApiKey": "",
  "Agents": {
    "QuerySynthesis": { "ModelDeploymentName": "gpt-5-4-mini" },
    "RelevanceEval":  { "ModelDeploymentName": "gpt-5-4" },
    "ImpactArea":     { "ModelDeploymentName": "gpt-5-4" },
    "Tags":           { "ModelDeploymentName": "gpt-5-4-mini" },
    "CompanyView":    { "ModelDeploymentName": "gpt-5-4" }
  }
}
```

Any agent omitted from `Agents` (or with a blank `ModelDeploymentName`) uses the top-level
`Foundry:ModelDeploymentName`.

## Relevant files

- [FoundryOptions.cs](../AgenticRagScannerApi/Configuration/FoundryOptions.cs) — new `Agents` map + `ResolveModel`
- [ServiceCollectionExtensions.cs](../AgenticRagScannerApi/Extensions/ServiceCollectionExtensions.cs) — `AddFoundryChatClient` + `AddWorkflowServices`
- `AgenticRagScannerApi/Services/` — new `IChatClientFactory` / `ChatClientFactory`
- [appsettings.json](../AgenticRagScannerApi/appsettings.json), `appsettings.Local.json.example`, [README.md](../README.md)

## Verification

1. Run the **build** task (`dotnet build`).
2. Run the **run-tests** task (`dotnet test`) — existing agent tests are unaffected (they inject a
   fake `IChatClient` directly).
3. Add an `OptionsValidationTests` case asserting `ResolveModel` returns the per-agent model when set
   and the fallback otherwise.
4. Manual smoke: set one agent's model in `appsettings.Local.json`, boot the API, confirm
   `ValidateOnStart` passes.

## Considerations

- Because infra is unchanged, any per-agent `ModelDeploymentName` set in config **must already exist**
  in the Foundry account, or calls will 404. Consider adding a startup log line listing each agent's
  resolved deployment to make misconfiguration obvious.
