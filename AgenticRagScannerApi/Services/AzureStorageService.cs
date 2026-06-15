using AgenticRagScannerApi.Configuration;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Services;

/// <inheritdoc />
public class AzureStorageService : IAzureStorageService
{
    private readonly AzureStorageOptions _options;
    private readonly ILogger<AzureStorageService> _logger;

    public AzureStorageService(IOptions<AzureStorageOptions> options, ILogger<AzureStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<Uri> UploadBlobAsync(string containerName, string blobName, Stream content, CancellationToken cancellationToken = default)
    {
        // TODO: implement using BlobServiceClient (prefer DefaultAzureCredential).
        throw new NotImplementedException();
    }
}
