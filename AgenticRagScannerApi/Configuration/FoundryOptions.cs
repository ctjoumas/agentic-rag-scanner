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
    /// When true, an HTTP <c>Retry-After</c> header on a throttled/overloaded response (429/503/529)
    /// overrides the exponential backoff for that retry, so the client waits exactly as long as the
    /// service asks instead of hammering it. Default true.
    /// </summary>
    public bool RespectRetryAfter { get; set; } = true;

    /// <summary>
    /// Fraction of calls (0.0-1.0) that must fail within the sampling window before the circuit breaker
    /// opens and short-circuits further calls (fail fast instead of piling onto an overloaded endpoint).
    /// Default 0.5.
    /// </summary>
    [Range(0.0, 1.0)]
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>Rolling window (seconds) over which <see cref="CircuitBreakerFailureRatio"/> is measured. Default 30.</summary>
    [Range(1, 3600)]
    public int CircuitBreakerSamplingSeconds { get; set; } = 30;

    /// <summary>Minimum calls in the sampling window before the breaker can trip (avoids opening on tiny samples). Default 10.</summary>
    [Range(1, 10000)]
    public int CircuitBreakerMinimumThroughput { get; set; } = 10;

    /// <summary>How long (seconds) the breaker stays open before probing with a trial call. Default 15.</summary>
    [Range(1, 3600)]
    public int CircuitBreakerBreakSeconds { get; set; } = 15;

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
