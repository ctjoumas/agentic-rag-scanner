using System.Text;
using AgenticRagScannerApi.Configuration;
using AgenticRagScannerApi.Workflows.Pipeline;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Services;

/// <summary>
/// Blob-backed <see cref="IFullTextStore"/> (Epic 5, story 5.4): writes the cleaned full text the
/// Relevance Eval agent read to the private documents container under a deterministic, idempotent key,
/// and returns the blob reference for <c>ResultItem.FullTextBlobUri</c>. Bytes are never stored inline
/// in Cosmos.
/// </summary>
public sealed class FullTextStore : IFullTextStore
{
    private readonly IAzureStorageService _storage;
    private readonly AzureStorageOptions _options;

    public FullTextStore(IAzureStorageService storage, IOptions<AzureStorageOptions> options)
    {
        _storage = storage;
        _options = options.Value;
    }

    public async Task<string> PersistAsync(string runId, string groupId, string itemId, string text, CancellationToken cancellationToken = default)
    {
        var blobName = $"fulltext/{runId}/{groupId}/{itemId}.txt";

        using var content = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var uri = await _storage.UploadBlobAsync(_options.DocumentsContainer, blobName, content, cancellationToken);

        return uri.ToString();
    }
}
