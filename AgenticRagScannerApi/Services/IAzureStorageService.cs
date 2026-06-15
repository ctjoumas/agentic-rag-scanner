namespace AgenticRagScannerApi.Services;

/// <summary>
/// Abstraction over the Azure Storage account (blob storage for fetched
/// source documents and generated exports).
/// </summary>
public interface IAzureStorageService
{
    /// <summary>Uploads content to a blob and returns its URI.</summary>
    Task<Uri> UploadBlobAsync(string containerName, string blobName, Stream content, CancellationToken cancellationToken = default);
}
