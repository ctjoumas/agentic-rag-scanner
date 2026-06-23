namespace AgenticRagScannerApi.Workflows.Pipeline;

/// <summary>
/// Persists the cleaned full text the Relevance Eval agent read to a private blob, so the exact
/// snapshot survives even if the live URL changes or 404s (audit/provenance). The loop only keeps the
/// returned reference (path/URI) on <c>ResultItem.FullTextBlobUri</c>; the bytes never go inline into
/// Cosmos. Implemented in the API host (over Azure Blob Storage); kept as an abstraction here so the
/// Workflows loop stays free of storage-SDK dependencies and is unit-testable.
/// </summary>
public interface IFullTextStore
{
    /// <summary>
    /// Writes <paramref name="text"/> under the deterministic, idempotent key
    /// <c>fulltext/{runId}/{groupId}/{itemId}.txt</c> and returns the blob reference (NOT a SAS link).
    /// </summary>
    Task<string> PersistAsync(string runId, string groupId, string itemId, string text, CancellationToken cancellationToken = default);
}
