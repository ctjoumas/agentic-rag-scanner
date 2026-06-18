namespace AgenticRagScannerApi.Workflows.Prompts;

/// <summary>
/// System-prompt builder for the Summarize &amp; Impact agent. Placeholder for Epic 2 (the agent is
/// stubbed and makes no LLM call); Epic 7 fills in the real instructions and wires the agent to
/// Foundry. Prompts are versioned so eval runs can attribute output changes to prompt changes.
/// </summary>
public static class SummarizeImpactPrompt
{
    /// <summary>Prompt version - bump when the instructions change.</summary>
    public const string Version = "v0-stub";

    /// <summary>Builds the system prompt. Placeholder until Epic 7.</summary>
    public static string BuildSystemPrompt() =>
        "TODO (Epic 7): instruct the model to write a plain-English impact summary grounded in the " +
        "in-memory search history, framing the change around its effective date for the audience.";
}
