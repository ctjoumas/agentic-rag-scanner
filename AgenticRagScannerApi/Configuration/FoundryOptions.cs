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

    /// <summary>
    /// Optional per-agent model deployment overrides, keyed by agent name (see <see cref="FoundryAgentKeys"/>).
    /// Every agent shares this Foundry endpoint; only the model deployment differs. An agent absent from
    /// this map (or with a blank override) falls back to <see cref="ModelDeploymentName"/>.
    /// </summary>
    public Dictionary<string, FoundryAgentOptions> Agents { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the model deployment for the given agent key: the per-agent override when configured and
    /// non-blank, otherwise the shared default <see cref="ModelDeploymentName"/>.
    /// </summary>
    public string ResolveModel(string agentKey)
    {
        if (Agents.TryGetValue(agentKey, out var agent) && !string.IsNullOrWhiteSpace(agent.ModelDeploymentName))
        {
            return agent.ModelDeploymentName;
        }

        return ModelDeploymentName;
    }
}

/// <summary>Per-agent Foundry overrides bound from "Foundry:Agents:&lt;AgentName&gt;".</summary>
public class FoundryAgentOptions
{
    /// <summary>Model deployment for this agent; when null/blank the shared default is used.</summary>
    public string? ModelDeploymentName { get; set; }
}

/// <summary>Canonical agent keys used both as configuration keys under "Foundry:Agents" and in DI wiring.</summary>
public static class FoundryAgentKeys
{
    public const string QuerySynthesis = "QuerySynthesis";
    public const string RelevanceEval = "RelevanceEval";
    public const string ImpactArea = "ImpactArea";
    public const string Tags = "Tags";
    public const string CompanyView = "CompanyView";
}
