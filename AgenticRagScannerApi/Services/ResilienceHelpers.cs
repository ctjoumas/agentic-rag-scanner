using System.ClientModel;
using System.Globalization;
using AgenticRagScannerApi.Core.Throttling;
using Azure;

namespace AgenticRagScannerApi.Services;

/// <summary>
/// Shared resilience helpers used by the Foundry chat pipeline (<see cref="ResilientChatClient"/>) and the
/// Web Search agent pipeline: classifying transient failures and reading an HTTP <c>Retry-After</c> hint off
/// the underlying Azure/OpenAI exception so Polly can honor the service's requested back-off (e.g. on a
/// 429 throttle or a 529 overloaded response) instead of using blind exponential backoff.
/// </summary>
internal static class ResilienceHelpers
{
    /// <summary>
    /// True for the transient infrastructure surface both pipelines retry: connection drops (status 0),
    /// request timeouts (408), throttling (429), and server-side failures (>= 500, which includes the
    /// 529 "overloaded" response), plus <see cref="HttpRequestException"/> / <see cref="TimeoutException"/>.
    /// Non-transient failures (4xx other than 408/429) are surfaced immediately.
    /// </summary>
    public static bool IsTransient(Exception? exception) => exception switch
    {
        ClientResultException clientResult => clientResult.Status is 0 or 408 or 429 or >= 500,
        RequestFailedException requestFailed => requestFailed.Status is 0 or 408 or 429 or >= 500,
        HttpRequestException => true,
        TimeoutException => true,
        // The shared throttle rejected the call because its wait queue is full - back off and retry.
        ThrottleRejectedException => true,
        _ => false,
    };

    /// <summary>
    /// Failures the circuit breaker should count toward tripping: the same transient surface as
    /// <see cref="IsTransient"/>, but excluding <see cref="ThrottleRejectedException"/>. A throttle
    /// rejection is our own backpressure (load shedding), not an unhealthy endpoint - it is still retried
    /// (see <see cref="IsTransient"/>), but must not open the breaker, or a demand spike against a healthy
    /// service would fail-fast every call for the break duration.
    /// </summary>
    public static bool ShouldBreak(Exception? exception) =>
        IsTransient(exception) && exception is not ThrottleRejectedException;

    /// <summary>
    /// Reads the <c>Retry-After</c> header from a failed response, supporting both the delta-seconds form
    /// ("Retry-After: 12") and the HTTP-date form ("Retry-After: Wed, 21 Oct 2026 07:28:00 GMT"). Returns
    /// <see langword="null"/> when the header is absent, unparseable, or non-positive.
    /// </summary>
    public static TimeSpan? TryGetRetryAfter(Exception? exception)
    {
        var raw = exception switch
        {
            RequestFailedException requestFailed => GetHeader(requestFailed),
            ClientResultException clientResult => GetHeader(clientResult),
            _ => null,
        };

        return ParseRetryAfter(raw);
    }

    private static string? GetHeader(RequestFailedException exception)
    {
        var response = exception.GetRawResponse();
        if (response is not null && response.Headers.TryGetValue("Retry-After", out var value))
        {
            return value;
        }

        return null;
    }

    private static string? GetHeader(ClientResultException exception)
    {
        var response = exception.GetRawResponse();
        if (response is not null && response.Headers.TryGetValue("Retry-After", out var value))
        {
            return value;
        }

        return null;
    }

    private static TimeSpan? ParseRetryAfter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Delta-seconds form.
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;
        }

        // HTTP-date form.
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when))
        {
            var delta = when - DateTimeOffset.UtcNow;
            return delta > TimeSpan.Zero ? delta : null;
        }

        return null;
    }
}
