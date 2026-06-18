namespace AgenticRagScannerApi.Workflows.Prompts;

/// <summary>
/// System-prompt builder for the Relevance Eval agent. Placeholder for Epic 2 (the agent is stubbed
/// and makes no LLM call); Epic 6 fills in the real instructions and wires the agent to Foundry.
/// Prompts are versioned so eval runs can attribute output changes to prompt changes.
/// </summary>
public static class RelevanceEvalPrompt
{
    /// <summary>Prompt version - bump when the instructions change.</summary>
    public const string Version = "v0-stub";

    /// <summary>Builds the system prompt for the given group/as-of date. Placeholder until Epic 6.</summary>
    public static string BuildSystemPrompt(string groupName, string asOfDate) =>
        $"TODO (Epic 6): instruct the model to classify each document for the '{groupName}' group as " +
        $"RELEVANT / BORDERLINE / NOT_RELEVANT using the full text, distinguishing publication vs " +
        $"effective vs tax-year dates relative to {asOfDate}, and to judge whether the group's goal is met.";
}
