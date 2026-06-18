using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Steps;

/// <inheritdoc />
public sealed class PreFilterStep : IPreFilterStep
{
    private readonly ILogger<PreFilterStep> _logger;

    public PreFilterStep(ILogger<PreFilterStep> logger) => _logger = logger;

    public IReadOnlyList<SearchHit> Filter(IReadOnlyList<SearchHit> hits, SearchHistory history)
    {
        var kept = new List<SearchHit>(hits.Count);

        foreach (var hit in hits)
        {
            var key = NormalizeUrl(hit.Url);
            if (key is null)
            {
                continue; // invalid / non-http(s) URL dropped
            }

            if (!history.ProcessedKeys.Add(key))
            {
                continue; // duplicate (this pass or an earlier one) dropped
            }

            kept.Add(hit);
        }

        _logger.LogDebug("Pre-filter: {InCount} hit(s) -> {OutCount} kept.", hits.Count, kept.Count);

        return kept;
    }

    /// <summary>Normalizes an absolute http(s) URL to a host+path dedupe key, or null if invalid.</summary>
    private static string? NormalizeUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? $"{uri.Host}{uri.AbsolutePath}".TrimEnd('/').ToLowerInvariant()
            : null;
}
