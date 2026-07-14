namespace AgenticRagScannerApi.Services;

/// <summary>
/// Abstraction over the Azure Storage account (blob storage for fetched
/// source documents and generated exports).
/// </summary>
public interface IAzureStorageService
{
    /// <summary>Uploads content to a blob and returns its URI.</summary>
    Task<Uri> UploadBlobAsync(string containerName, string blobName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Downloads a blob's UTF-8 text, or <see langword="null"/> when the blob does not exist.</summary>
    Task<string?> DownloadBlobTextAsync(string containerName, string blobName, CancellationToken cancellationToken = default);
}
