using System.ComponentModel.DataAnnotations;

namespace AgenticRagScannerApi.Workflows.Configuration;

/// <summary>
/// Settings for the Web Search agent (Epic 4, story 4.1). The agent is a hosted Foundry agent that
/// carries a Grounding with Bing Custom Search tool, so grounding is restricted to the curated
/// domains defined by the Bing Custom Search configuration (the customer's primary-source allowlist).
/// <see cref="ProjectEndpoint"/> is the Foundry <em>project</em> endpoint and is distinct from
/// <c>FoundryOptions.Endpoint</c>, which is the Azure OpenAI inference endpoint used by the chat agents.
/// </summary>
public sealed class WebSearchOptions
{
    public const string SectionName = "WebSearch";

    /// <summary>Foundry project endpoint backing the <c>AIProjectClient</c> (NOT the AOAI inference endpoint).</summary>
    [Required]
    [Url]
    public string ProjectEndpoint { get; set; } = string.Empty;

    /// <summary>Model deployment the Web Search agent runs on.</summary>
    [Required]
    public string ModelDeploymentName { get; set; } = string.Empty;

    /// <summary>Display name for the hosted Web Search agent.</summary>
    [Required]
    public string AgentName { get; set; } = "WebSearch";

    /// <summary>
    /// ARM connection id for the Bing Custom Search resource
    /// (e.g. <c>/subscriptions/.../projects/&lt;name&gt;/connections/&lt;connection-name&gt;</c>).
    /// </summary>
    [Required]
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// Bing Custom Search configuration ("instance") name that defines which sites/domains to search.
    /// This is the configuration name from the Bing Custom Search resource, not the Azure resource name.
    /// </summary>
    [Required]
    public string InstanceName { get; set; } = string.Empty;

    /// <summary>Maximum number of search hits to surface per query (after de-duplication).</summary>
    [Range(1, 50)]
    public int MaxResults { get; set; } = 10;

    /// <summary>System instructions for the agent; the default asks it to search and cite authoritative sources.</summary>
    [Required]
    public string Instructions { get; set; } =
        "You are a web search assistant for a regulatory horizon scanner. For each user query, use the " +
        "Bing Custom Search tool to find authoritative primary sources, and always cite every source you use.";

    /// <summary>Maximum retry attempts the resilience pipeline makes on transient Web Search agent failures.</summary>
    [Range(0, 10)]
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay (seconds) for the resilience pipeline's exponential backoff between retries.</summary>
    [Range(0.0, 60.0)]
    public double RetryBaseDelaySeconds { get; set; } = 2.0;

    /// <summary>Per-request timeout (seconds) the resilience pipeline enforces on each Web Search agent call.</summary>
    [Range(1, 600)]
    public int RequestTimeoutSeconds { get; set; } = 100;
}
