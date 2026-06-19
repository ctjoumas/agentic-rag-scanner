using System.ComponentModel.DataAnnotations;

namespace AgenticRagScannerApi.Configuration;

/// <summary>
/// Binds to the "Foundry" configuration section (Microsoft Foundry — hosts the
/// models used for all LLM calls: query synthesis, eval, categorize, summarize).
/// </summary>
public class FoundryOptions
{
    public const string SectionName = "Foundry";

    /// <summary>
    /// Azure OpenAI inference endpoint of the Foundry resource that hosts the model deployment
    /// (e.g. https://&lt;resource&gt;.openai.azure.com/ or https://&lt;resource&gt;.cognitiveservices.azure.com/).
    /// The chat client is built directly against this endpoint, so no project-connection lookup is needed.
    /// </summary>
    [Required]
    [Url]
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Model deployment used for the agent LLM calls.</summary>
    [Required]
    public string ModelDeploymentName { get; set; } = string.Empty;

    /// <summary>
    /// API key for local development.
    /// Prefer DefaultAzureCredential in deployed environments.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Maximum retry attempts the resilience pipeline makes on transient Foundry failures.</summary>
    [Range(0, 10)]
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay (seconds) for the resilience pipeline's exponential backoff between retries.</summary>
    [Range(0.0, 60.0)]
    public double RetryBaseDelaySeconds { get; set; } = 2.0;

    /// <summary>Per-request timeout (seconds) the resilience pipeline enforces on each Foundry call.</summary>
    [Range(1, 600)]
    public int RequestTimeoutSeconds { get; set; } = 100;
}
