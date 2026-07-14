using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Core.Throttling;
using AgenticRagScannerApi.Workflows.Configuration;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;

namespace AgenticRagScannerApi.Workflows.Tools;

/// <summary>
/// Epic 4 (story 4.1) real implementation of <see cref="IWebSearchAgent"/>. It runs a pre-provisioned
/// Foundry Web Search agent (an <see cref="AIAgent"/> resolved by name from the portal, configured there
/// with Grounding with Bing Custom Search) for the synthesized query and maps the response's URL
/// citations into <see cref="SearchHit"/>s. Grounding is already scoped to the customer's curated domains
/// by the agent's Bing Custom Search configuration; the allowlist check here is defense-in-depth. The
/// agent never throws to abort a run - on failure or zero citations it logs and returns an empty list,
/// letting the loop controller decide what to do next. The Foundry-specific agent resolution lives in the
/// composition root (DI), so this class depends only on the MAF <see cref="AIAgent"/> abstraction and is
/// fully unit-testable with a fake agent.
/// </summary>
public sealed class WebSearchAgent : IWebSearchAgent
{
    private readonly AIAgent _agent;
    private readonly WebSearchOptions _options;
    private readonly ISharedThrottle _throttle;
    private readonly ResiliencePipeline _resilience;
    private readonly ILogger<WebSearchAgent> _logger;

    public WebSearchAgent(
        AIAgent agent,
        IOptions<WebSearchOptions> options,
        ISharedThrottle throttle,
        ResiliencePipeline resilience,
        ILogger<WebSearchAgent> logger)
    {
        _agent = agent;
        _options = options.Value;
        _throttle = throttle;
        _resilience = resilience;
        _logger = logger;
    }

    public async Task<WebSearchResult> SearchAsync(string query, RunContext run, CancellationToken cancellationToken = default)
    {
        var allowedHosts = BuildAllowedHosts(run.AuthoritativeSources);

        try
        {
            // Stream the hosted agent's run rather than issuing one long synchronous "create response"
            // call: a Bing-grounded run (model reasoning + web search + grounding) can exceed the
            // service's synchronous-response window and come back as HTTP 408. Streaming keeps the
            // connection producing incremental updates, which avoids that server-side timeout; the updates
            // are then aggregated back into a single response so the citation extraction below is
            // unchanged. Retry transient failures (with a per-attempt timeout) and funnel each attempt
            // through the shared throttle so N parallel topic groups respect Bing QPS. Pipeline outer,
            // throttle inner mirrors ResilientChatClient so a retried attempt re-acquires a throttle permit.
            var response = await _resilience.ExecuteAsync(
                async ct => await _throttle.ExecuteAsync(
                    async t => await _agent
                        .RunStreamingAsync(query, cancellationToken: t)
                        .ToAgentResponseAsync(t)
                        .ConfigureAwait(false),
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
                if (!TryExtractCitation(annotation, out var url, out var title))
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

            // A successful run - even one that legitimately returned zero citations - is NOT a failure.
            return WebSearchResult.Ok(hits);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // A cancellation whose token is NOT the caller's comes from the SDK network timeout
            // (AIProjectClient.NetworkTimeout). Treat it like any other failed search: log a concise
            // warning and degrade gracefully rather than aborting the run - but surface it as a failure
            // so a zero-result group is not silently reported as a clean, completed empty scan.
            _logger.LogWarning(
                ex,
                "WebSearch: query '{Query}' was canceled by an SDK network timeout; returning no hits.",
                query);
            return WebSearchResult.Failure("Web search was canceled by an SDK network timeout.");
        }
        catch (TimeoutRejectedException)
        {
            // Expected, handled condition: the hosted agent didn't respond within the per-attempt
            // timeout. The loop controller proceeds without these hits, so log a concise warning
            // (not the full transport stack trace) and degrade gracefully - surfaced as a failure.
            _logger.LogWarning(
                "WebSearch: query '{Query}' timed out after the configured per-request timeout; returning no hits.",
                query);
            return WebSearchResult.Failure("Web search timed out after the configured per-request timeout.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebSearch: agent run failed for query '{Query}'; returning no hits.", query);
            return WebSearchResult.Failure($"Web search agent run failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts a URL citation from a MAF annotation. Grounding with Bing surfaces sources as
    /// <see cref="CitationAnnotation"/> entries carrying the resolved URL and title.
    /// </summary>
    private static bool TryExtractCitation(AIAnnotation annotation, out string url, out string? title)
    {
        if (annotation is CitationAnnotation citation && citation.Url is { } uri)
        {
            url = uri.ToString();
            title = citation.Title;
            return true;
        }

        url = string.Empty;
        title = null;
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
