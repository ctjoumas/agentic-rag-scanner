using System.ComponentModel.DataAnnotations;

namespace AgenticRagScannerApi.Configuration;

/// <summary>
/// Binds to the "BingSearchGrounding" configuration section
/// (Grounding with Bing Search).
/// </summary>
public class BingSearchGroundingOptions
{
    public const string SectionName = "BingSearchGrounding";

    /// <summary>Foundry connection id for the Grounding with Bing Search resource.</summary>
    [Required]
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>API key for local development, if applicable.</summary>
    public string? ApiKey { get; set; }
}
