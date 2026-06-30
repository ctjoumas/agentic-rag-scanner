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

    /// <summary>
    /// Name of the pre-provisioned hosted Web Search agent (created in the Foundry portal with the
    /// Grounding with Bing Custom Search tool attached). The hosted agent owns its model, instructions,
    /// and tools; this app references it by name and resolves the latest version unless
    /// <see cref="AgentVersion"/> is set.
    /// </summary>
    [Required]
    public string AgentName { get; set; } = string.Empty;

    /// <summary>Optional hosted-agent version to pin; when empty the latest version is resolved by name.</summary>
    public string? AgentVersion { get; set; }

    /// <summary>Maximum number of search hits to surface per query (after de-duplication).</summary>
    [Range(1, 50)]
    public int MaxResults { get; set; } = 10;

    /// <summary>Maximum retry attempts the resilience pipeline makes on transient Web Search agent failures.</summary>
    [Range(0, 10)]
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay (seconds) for the resilience pipeline's exponential backoff between retries.</summary>
    [Range(0.0, 60.0)]
    public double RetryBaseDelaySeconds { get; set; } = 2.0;

    /// <summary>
    /// Per-request timeout (seconds). Drives both the SDK network timeout on the Foundry
    /// <c>AIProjectClient</c> (raising it above the 100s default, since Bing-grounded agent runs can
    /// exceed that) and the resilience pipeline's per-attempt backstop.
    /// </summary>
    [Range(1, 600)]
    public int RequestTimeoutSeconds { get; set; } = 240;
}
