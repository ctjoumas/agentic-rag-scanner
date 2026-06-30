namespace AgenticRagScannerApi.Core.Contracts;

/// <summary>
/// A single Bing search hit (already scoped to the customer's primary-source allowlist),
/// captured before fetch/eval. Our analog of the reference repo's SearchResult.
/// </summary>
public sealed class SearchHit
{
    /// <summary>Result URL (the dedupe key after normalization).</summary>
    public required string Url { get; init; }

    /// <summary>Result title, if provided.</summary>
    public string? Title { get; init; }

    /// <summary>Host/domain (used for allowlist checks and level-of-authority hints).</summary>
    public string? Domain { get; init; }

    /// <summary>The synthesized query that surfaced this hit (provenance).</summary>
    public required string SourceQuery { get; init; }

    /// <summary>Rank/position within the pass's results.</summary>
    public int Rank { get; init; }
}
