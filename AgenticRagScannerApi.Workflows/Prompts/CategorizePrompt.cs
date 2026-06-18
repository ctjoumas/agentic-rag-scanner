namespace AgenticRagScannerApi.Workflows.Prompts;

/// <summary>
/// System-prompt builder for the Categorize agent. Placeholder for Epic 2 (the agent is stubbed and
/// makes no LLM call); Epic 7 fills in the real instructions and wires the agent to Foundry. Prompts
/// are versioned so eval runs can attribute output changes to prompt changes.
/// </summary>
public static class CategorizePrompt
{
    /// <summary>Prompt version - bump when the instructions change.</summary>
    public const string Version = "v0-stub";

    /// <summary>Builds the system prompt from the approved tag vocabulary. Placeholder until Epic 7.</summary>
    public static string BuildSystemPrompt(IReadOnlyList<string> approvedTags) =>
        "TODO (Epic 7): instruct the model to assign an impact area, the responsible regulator, and " +
        $"tags drawn ONLY from the controlled vocabulary [{string.Join(", ", approvedTags)}].";
}
