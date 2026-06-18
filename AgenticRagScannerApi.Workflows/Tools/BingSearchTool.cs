using System.Text.RegularExpressions;
using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Tools;

/// <inheritdoc />
public sealed class BingSearchTool : IBingSearchTool
{
    private const string DefaultAllowlistHost = "www.gov.uk";

    private static readonly Regex SlugPattern = new("[^a-z0-9]+", RegexOptions.Compiled);

    private readonly ILogger<BingSearchTool> _logger;

    public BingSearchTool(ILogger<BingSearchTool> logger) => _logger = logger;

    public Task<IReadOnlyList<SearchHit>> SearchAsync(string query, RunContext run, CancellationToken cancellationToken = default)
    {
        // Allowlist hook: scope canned results to the run's primary-source allowlist when present.
        // Epic 4 replaces these canned hits with Grounding with Bing Search restricted to the
        // allowlist at query time.
        IReadOnlyList<string> allowlist = run.AuthoritativeSources.Count > 0
            ? run.AuthoritativeSources
            : new[] { DefaultAllowlistHost };

        var hits = new List<SearchHit>(3);
        for (var i = 0; i < 3; i++)
        {
            var host = ExtractHost(allowlist[i % allowlist.Count]);
            hits.Add(new SearchHit
            {
                Url = $"https://{host}/canned/{Slug(query)}/{i}",
                Title = $"Canned result {i + 1} for '{query}'",
                Snippet = $"Canned Bing snippet (Epic 2) for '{query}'.",
                Domain = host,
                SourceQuery = query,
                Rank = i + 1,
            });
        }

        _logger.LogDebug(
            "Bing search tool stub: query '{Query}' -> {Count} canned hit(s) (allowlist size {AllowlistSize}).",
            query, hits.Count, run.AuthoritativeSources.Count);

        return Task.FromResult<IReadOnlyList<SearchHit>>(hits);
    }

    /// <summary>Extracts the host from an allowlist entry that may be a full URL or a bare host.</summary>
    private static string ExtractHost(string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri.Host : source.Trim('/');

    private static string Slug(string text)
    {
        var slug = SlugPattern.Replace(text.ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "q" : slug;
    }
}
