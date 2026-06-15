namespace AgenticRagScannerApi.Services;

/// <summary>
/// Abstraction over Grounding with Bing Search, constrained to the customer's
/// primary-source allowlist.
/// </summary>
public interface IBingSearchGroundingService
{
    /// <summary>Runs a grounded Bing search and returns the result references.</summary>
    Task<IReadOnlyList<string>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
