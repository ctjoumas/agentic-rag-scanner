using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Core.Throttling;
using AgenticRagScannerApi.Workflows.Configuration;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace AgenticRagScannerApi.Workflows.Tools;

/// <summary>
/// Epic 4 (story 4.1) real implementation of <see cref="IBingSearchTool"/>. It runs a hosted Foundry
/// Web Search agent (an <see cref="AIAgent"/> carrying a Grounding with Bing Custom Search tool) for the
/// synthesized query and maps the response's URL citations into <see cref="SearchHit"/>s. Grounding is
/// already scoped to the customer's curated domains by the Bing Custom Search configuration; the
/// allowlist check here is defense-in-depth. The agent never throws to abort a run - on failure or zero
/// citations it logs and returns an empty list, letting the loop controller decide what to do next.
/// The Foundry-specific agent construction lives in the composition root (DI), so this class depends only
/// on the MAF <see cref="AIAgent"/> abstraction and is fully unit-testable with a fake agent.
/// </summary>
public sealed class BingGroundingWebSearchAgent : IBingSearchTool
{
    private readonly AIAgent _agent;
    private readonly WebSearchOptions _options;
    private readonly ISharedThrottle _throttle;
    private readonly ResiliencePipeline _resilience;
    private readonly ILogger<BingGroundingWebSearchAgent> _logger;

    public BingGroundingWebSearchAgent(
        AIAgent agent,
        IOptions<WebSearchOptions> options,
        ISharedThrottle throttle,
        ResiliencePipeline resilience,
        ILogger<BingGroundingWebSearchAgent> logger)
    {
        _agent = agent;
        _options = options.Value;
        _throttle = throttle;
        _resilience = resilience;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, RunContext run, CancellationToken cancellationToken = default)
    {
        var allowedHosts = BuildAllowedHosts(run.AuthoritativeSources);

        try
        {
            // Retry transient failures (with a per-attempt timeout) and funnel each attempt through the
            // shared throttle so N parallel topic groups respect Bing QPS. Pipeline outer, throttle inner
            // mirrors ResilientChatClient so a retried attempt re-acquires a throttle permit.
            var response = await _resilience.ExecuteAsync(
                async ct => await _throttle.ExecuteAsync(
                    t => _agent.RunAsync(query, cancellationToken: t),
                    permits: 1,
                    cancellationToken: ct).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);

            var hits = new List<SearchHit>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var droppedOffAllowlist = 0;
            var rank = 0;

            foreach (var annotation in response.Messages
                .SelectMany(m => m.Contents)
                .SelectMany(c => c.Annotations ?? []))
            {
                if (!TryExtractCitation(annotation, out var url, out var title, out var snippet))
                {
                    continue;
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    continue;
                }

                if (allowedHosts.Count > 0 && !allowedHosts.Contains(NormalizeHost(uri.Host)))
                {
                    droppedOffAllowlist++;
                    continue;
                }

                if (!seen.Add(url))
                {
                    continue;
                }

                hits.Add(new SearchHit
                {
                    Url = url,
                    Title = title,
                    Snippet = snippet,
                    Domain = uri.Host,
                    SourceQuery = query,
                    Rank = ++rank,
                });

                if (hits.Count >= _options.MaxResults)
                {
                    break;
                }
            }

            if (droppedOffAllowlist > 0)
            {
                _logger.LogWarning(
                    "WebSearch: query '{Query}' returned {Dropped} citation(s) outside the allowlist; dropped them.",
                    query, droppedOffAllowlist);
            }

            _logger.LogInformation(
                "WebSearch: query '{Query}' -> {Count} hit(s) (allowlist size {AllowlistSize}).",
                query, hits.Count, allowedHosts.Count);

            return hits;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebSearch: agent run failed for query '{Query}'; returning no hits.", query);
            return [];
        }
    }

    /// <summary>
    /// Extracts a URL citation from a MAF annotation. Grounding with Bing surfaces sources as
    /// <see cref="CitationAnnotation"/> entries carrying the resolved URL, title, and snippet.
    /// </summary>
    private static bool TryExtractCitation(AIAnnotation annotation, out string url, out string? title, out string? snippet)
    {
        if (annotation is CitationAnnotation citation && citation.Url is { } uri)
        {
            url = uri.ToString();
            title = citation.Title;
            snippet = citation.Snippet;
            return true;
        }

        url = string.Empty;
        title = null;
        snippet = null;
        return false;
    }

    /// <summary>
    /// Builds the set of allowed hosts from the run's primary-source allowlist. Entries may be full URLs
    /// or bare hosts; both are normalized to a comparable host key. An empty allowlist means no extra
    /// filtering (the hosted Bing Custom Search tool already scopes grounding to the configured domains).
    /// </summary>
    private static HashSet<string> BuildAllowedHosts(IReadOnlyList<string> authoritativeSources)
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in authoritativeSources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var host = Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri.Host : source.Trim().Trim('/');
            if (!string.IsNullOrEmpty(host))
            {
                hosts.Add(NormalizeHost(host));
            }
        }

        return hosts;
    }

    /// <summary>Lowercases the host and strips a leading <c>www.</c> so allowlist matching is stable.</summary>
    private static string NormalizeHost(string host)
    {
        host = host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}
