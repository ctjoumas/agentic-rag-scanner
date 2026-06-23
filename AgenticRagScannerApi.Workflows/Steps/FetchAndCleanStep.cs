using System.Net.Http.Headers;
using System.Text;
using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Workflows.Configuration;
using AgenticRagScannerApi.Workflows.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Workflows.Steps;

/// <inheritdoc />
public sealed class FetchAndCleanStep : IFetchAndCleanStep
{
    /// <summary>Named <see cref="HttpClient"/> configured with the SSRF-guarded primary handler.</summary>
    public const string HttpClientName = "fetch";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FetchOptions _options;
    private readonly ILogger<FetchAndCleanStep> _logger;

    public FetchAndCleanStep(
        IHttpClientFactory httpClientFactory,
        IOptions<FetchOptions> options,
        ILogger<FetchAndCleanStep> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FetchedDocument> FetchAsync(SearchHit hit, CancellationToken cancellationToken = default)
    {
        // Cheap scheme check only - the fetch targets are the customer's curated primary-source domains
        // (already gated by Grounding with Bing Custom Search), so a full SSRF guard is deferred (Epic 11).
        if (!Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _logger.LogWarning(
                "Fetch skipped for {Url} (not an absolute http/https URL); falling back to snippet.",
                hit.Url);
            return Fallback(hit);
        }

        // Bound the whole fetch by the configured timeout. A caller-initiated cancellation propagates;
        // a timeout is just another fetch failure and degrades to the snippet fallback.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.RequestTimeout);

        try
        {
            return await FetchCoreAsync(uri, hit, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // genuine caller cancellation - do not swallow.
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Fetch timed out after {Timeout}s for {Url}; falling back to snippet.",
                _options.RequestTimeoutSeconds, hit.Url);
            return Fallback(hit);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fetch failed for {Url}; falling back to snippet.", hit.Url);
            return Fallback(hit);
        }
    }

    private async Task<FetchedDocument> FetchCoreAsync(Uri uri, SearchHit hit, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        foreach (var contentType in _options.AllowedContentTypes)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(contentType));
        }

        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Fetch for {Url} returned {Status}; falling back to snippet.",
                hit.Url, (int)response.StatusCode);
            return Fallback(hit);
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null ||
            !_options.AllowedContentTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Fetch for {Url} returned disallowed content type '{ContentType}'; falling back to snippet.",
                hit.Url, mediaType ?? "(none)");
            return Fallback(hit);
        }

        if (response.Content.Headers.ContentLength is long declared && declared > _options.MaxResponseBytes)
        {
            _logger.LogWarning("Fetch for {Url} exceeds size cap ({Declared} > {Cap} bytes); falling back to snippet.",
                hit.Url, declared, _options.MaxResponseBytes);
            return Fallback(hit);
        }

        var bytes = await ReadCappedAsync(response.Content, _options.MaxResponseBytes, cancellationToken)
            .ConfigureAwait(false);
        if (bytes is null)
        {
            _logger.LogWarning("Fetch for {Url} exceeded size cap while streaming; falling back to snippet.", hit.Url);
            return Fallback(hit);
        }

        var cleaned = mediaType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            ? PdfTextExtractor.ExtractText(bytes)
            : HtmlTextExtractor.ExtractText(DecodeText(bytes, response.Content.Headers.ContentType));

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            _logger.LogWarning("Fetch for {Url} produced no usable text; falling back to snippet.", hit.Url);
            return Fallback(hit);
        }

        _logger.LogDebug("Fetched and cleaned {Length} chars from {Url}.", cleaned.Length, hit.Url);
        return new FetchedDocument
        {
            Hit = hit,
            CleanedText = cleaned,
            Unverified = false,
        };
    }

    /// <summary>
    /// Reads the body into memory, stopping (and returning null) once it would exceed
    /// <paramref name="maxBytes"/>. Guards against a server that omits or lies about Content-Length.
    /// </summary>
    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>Decodes HTML bytes using the charset from the Content-Type header, defaulting to UTF-8.</summary>
    private static string DecodeText(byte[] bytes, MediaTypeHeaderValue? contentType)
    {
        var charset = contentType?.CharSet;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset.Trim('"')).GetString(bytes);
            }
            catch (ArgumentException)
            {
                // Unknown/invalid charset - fall through to UTF-8.
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Snippet fallback: never drops the document. The Bing snippet becomes the (unverified) cleaned
    /// text; relevance/discard decisions happen later at the eval step (Epic 6), not here.
    /// </summary>
    private static FetchedDocument Fallback(SearchHit hit) => new()
    {
        Hit = hit,
        CleanedText = string.IsNullOrWhiteSpace(hit.Snippet) ? null : hit.Snippet,
        Unverified = true,
    };
}
