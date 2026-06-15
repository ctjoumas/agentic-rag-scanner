namespace AgenticRagScannerApi.Services;

/// <summary>
/// Abstraction over Grounding with Bing Custom Search (custom-scoped search
/// configuration / allowlist).
/// </summary>
public interface IBingCustomSearchGroundingService
{
    /// <summary>Runs a grounded Bing Custom Search and returns the result references.</summary>
    Task<IReadOnlyList<string>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
