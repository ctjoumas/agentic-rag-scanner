namespace AgenticRagScannerApi.Configuration;

/// <summary>
/// Binds to the "BingCustomSearchGrounding" configuration section
/// (Grounding with Bing Custom Search).
/// </summary>
public class BingCustomSearchGroundingOptions
{
    public const string SectionName = "BingCustomSearchGrounding";

    /// <summary>Foundry connection id for the Grounding with Bing Custom Search resource.</summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>Custom search configuration id (the configured instance / allowlist).</summary>
    public string CustomConfigurationId { get; set; } = string.Empty;

    /// <summary>API key for local development, if applicable.</summary>
    public string? ApiKey { get; set; }
}
