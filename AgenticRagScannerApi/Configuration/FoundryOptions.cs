namespace AgenticRagScannerApi.Configuration;

/// <summary>
/// Binds to the "Foundry" configuration section (Microsoft Foundry — hosts the
/// models used for all LLM calls: query synthesis, eval, categorize, summarize).
/// </summary>
public class FoundryOptions
{
    public const string SectionName = "Foundry";

    /// <summary>Foundry project endpoint.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Model deployment used for the agent LLM calls.</summary>
    public string ModelDeploymentName { get; set; } = string.Empty;

    /// <summary>
    /// API key for local development.
    /// Prefer DefaultAzureCredential in deployed environments.
    /// </summary>
    public string? ApiKey { get; set; }
}
