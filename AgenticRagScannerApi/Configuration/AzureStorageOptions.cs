using System.ComponentModel.DataAnnotations;

namespace AgenticRagScannerApi.Configuration;

/// <summary>
/// Binds to the "AzureStorage" configuration section. Blob storage for fetched
/// source documents and generated exports.
/// </summary>
public class AzureStorageOptions
{
    public const string SectionName = "AzureStorage";

    /// <summary>
    /// Blob service endpoint, e.g. https://{account}.blob.core.windows.net.
    /// Prefer Managed Identity / DefaultAzureCredential over connection strings.
    /// </summary>
    [Required]
    [Url]
    public string BlobServiceUri { get; set; } = string.Empty;

    /// <summary>
    /// Optional connection string for local development only.
    /// Leave empty when authenticating with DefaultAzureCredential.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>Container for fetched source documents.</summary>
    [Required]
    public string DocumentsContainer { get; set; } = string.Empty;

    /// <summary>Container for generated exports (CSV/Excel).</summary>
    [Required]
    public string ExportsContainer { get; set; } = string.Empty;
}
