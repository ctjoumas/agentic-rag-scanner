namespace AgenticRagScannerApi.Workflows.Prompts;

/// <summary>
/// System-prompt builder for the Query Synthesis agent. Placeholder for Epic 2 (the agent is stubbed
/// and makes no LLM call); Epic 3 fills in the real instructions and wires the agent to Foundry.
/// Prompts are versioned so eval runs can attribute output changes to prompt changes.
/// </summary>
public static class QuerySynthesisPrompt
{
    /// <summary>Prompt version - bump when the instructions change.</summary>
    public const string Version = "v0-stub";

    /// <summary>Builds the system prompt for the given group/jurisdiction. Placeholder until Epic 3.</summary>
    public static string BuildSystemPrompt(string groupName, string jurisdiction) =>
        $"TODO (Epic 3): instruct the model to synthesize focused, non-redundant search queries for " +
        $"the '{groupName}' topic group in {jurisdiction}, rotating synonym coverage across passes " +
        $"using the in-memory search history to avoid repeating prior queries.";
}
