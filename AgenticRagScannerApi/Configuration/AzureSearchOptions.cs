namespace AgenticRagScannerApi.Configuration;

/// <summary>
/// Binds to the "AzureSearch" configuration section (Azure AI Search).
/// Reserved for the planned memory / learnings store.
/// </summary>
public class AzureSearchOptions
{
    public const string SectionName = "AzureSearch";

    /// <summary>Search service endpoint, e.g. https://{service}.search.windows.net.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Target index name.</summary>
    public string IndexName { get; set; } = string.Empty;

    /// <summary>
    /// Admin/query key for local development.
    /// Prefer DefaultAzureCredential in deployed environments.
    /// </summary>
    public string? ApiKey { get; set; }
}
