using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace AgenticRagScannerApi.Services;

/// <inheritdoc />
public class AzureStorageService : IAzureStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<AzureStorageService> _logger;

    public AzureStorageService(BlobServiceClient blobServiceClient, ILogger<AzureStorageService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    public async Task<Uri> UploadBlobAsync(string containerName, string blobName, Stream content, CancellationToken cancellationToken = default)
    {
        var container = _blobServiceClient.GetBlobContainerClient(containerName);

        // Containers stay private (no public access); created lazily so first-run/dev works.
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var blob = container.GetBlobClient(blobName);

        // Overwrite makes a deterministic key (e.g. fulltext/{runId}/{groupId}/{itemId}.txt) idempotent
        // across re-runs - the snapshot is replaced in place rather than duplicated.
        await blob.UploadAsync(content, overwrite: true, cancellationToken);

        _logger.LogDebug("Uploaded blob '{BlobName}' to container '{Container}'.", blobName, containerName);

        return blob.Uri;
    }

    public async Task<string?> DownloadBlobTextAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        var container = _blobServiceClient.GetBlobContainerClient(containerName);
        var blob = container.GetBlobClient(blobName);

        try
        {
            var response = await blob.DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToString();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Blob '{BlobName}' not found in container '{Container}'.", blobName, containerName);
            return null;
        }
    }
}
