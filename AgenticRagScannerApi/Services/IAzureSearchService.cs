namespace AgenticRagScannerApi.Services;

/// <summary>
/// Abstraction over Azure AI Search. Reserved for the planned memory /
/// learnings store.
/// </summary>
public interface IAzureSearchService
{
    /// <summary>Runs a search query and returns matching document identifiers or content.</summary>
    Task<IReadOnlyList<string>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
