using System.ComponentModel.DataAnnotations;

namespace AgenticRagScannerApi.Workflows.Configuration;

/// <summary>
/// Binds to the "Cosmos" configuration section. Cosmos DB backs MAF workflow checkpointing in the
/// <c>checkpoints</c> container (Epic 2); Epic 8 reuses the same account for the versioned result
/// store in a separate <c>results</c> container. The account is connected keyless via
/// <c>DefaultAzureCredential</c> - there is no connection-string/key option.
/// </summary>
public sealed class CosmosOptions
{
    public const string SectionName = "Cosmos";

    /// <summary>Cosmos DB account endpoint (e.g. https://&lt;account&gt;.documents.azure.com:443/).</summary>
    [Required]
    [Url]
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Database that holds the workflow containers.</summary>
    [Required]
    public string Database { get; set; } = string.Empty;

    /// <summary>Container for MAF workflow checkpoints (partitioned by checkpoint session id).</summary>
    [Required]
    public string CheckpointsContainer { get; set; } = "checkpoints";

    /// <summary>Container backing the generic document CRUD repository. Name supplied via config.</summary>
    [Required]
    public string RegDocsContainer { get; set; } = string.Empty;
}
