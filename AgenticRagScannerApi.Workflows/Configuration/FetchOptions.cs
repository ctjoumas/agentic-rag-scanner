using System.ComponentModel.DataAnnotations;

namespace AgenticRagScannerApi.Workflows.Configuration;

/// <summary>
/// Settings for the full-text Fetch & Clean step (Epic 5, story 5.2). These bound an individual
/// fetch so a single slow or oversized source cannot stall a run or exhaust memory. The fetch targets
/// are the customer's curated primary-source domains (already gated by Grounding with Bing Custom
/// Search), so a full SSRF guard (private-IP blocking, host allowlist) is deferred to Epic 11 - see
/// backlog story 11.6.
/// </summary>
public sealed class FetchOptions
{
    public const string SectionName = "Fetch";

    /// <summary>Content types the fetcher will parse; anything else falls back to the snippet.</summary>
    public IList<string> AllowedContentTypes { get; set; } = new List<string>
    {
        "text/html",
        "application/xhtml+xml",
        "application/pdf",
    };

    /// <summary>Hard cap on the response body the fetcher will read into memory (DoS guard).</summary>
    [Range(1, 100)]
    public int MaxResponseMegabytes { get; set; } = 10;

    /// <summary>Maximum number of HTTP redirects to follow.</summary>
    [Range(0, 20)]
    public int MaxRedirects { get; set; } = 5;

    /// <summary>Per-fetch timeout (connect + read). A timeout is treated as a fetch failure → snippet fallback.</summary>
    [Range(1, 600)]
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary><see cref="MaxResponseMegabytes"/> expressed in bytes.</summary>
    public long MaxResponseBytes => MaxResponseMegabytes * 1024L * 1024L;

    /// <summary><see cref="RequestTimeoutSeconds"/> expressed as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(RequestTimeoutSeconds);
}
