using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgenticRagScannerApi.Core.Throttling;

/// <summary>
/// The real <see cref="ISharedThrottle"/>: gates outbound, quota-limited calls (Azure OpenAI TPM/RPM,
/// Bing QPS) so N parallel topic-group workflows never exceed shared service limits. It composes two
/// <see cref="RateLimiter"/>s:
/// <list type="bullet">
/// <item>a <see cref="ConcurrencyLimiter"/> capping how many calls may be in flight at once, and</item>
/// <item>an optional <see cref="TokenBucketRateLimiter"/> enforcing a per-window request budget (RPM/QPS).</item>
/// </list>
/// Every acquire waits (backpressure) when a limit is reached; if the wait queue is also full it throws
/// <see cref="ThrottleRejectedException"/> so the caller's resilience pipeline can back off and retry.
/// <para>
/// This throttle limits <em>individual outbound calls</em>. Capping how many topic-group workflows run in
/// parallel is a separate concern owned by the orchestrator, so a group holding a worker slot never blocks
/// the very calls it needs to make (which would deadlock a single shared concurrency limiter).
/// </para>
/// </summary>
public sealed class RateLimitingThrottle : ISharedThrottle, IDisposable
{
    private readonly ConcurrencyLimiter _concurrency;
    private readonly TokenBucketRateLimiter? _rate;
    private readonly ILogger<RateLimitingThrottle> _logger;
    private readonly int _concurrencyLimit;
    private readonly int _queueLimit;
    private readonly int _requestsPerWindow;
    private readonly double _windowSeconds;

    public RateLimitingThrottle(RateLimitingThrottleSettings settings, ILogger<RateLimitingThrottle>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _logger = logger ?? NullLogger<RateLimitingThrottle>.Instance;
        _concurrencyLimit = Math.Max(1, settings.MaxConcurrentCalls);
        _queueLimit = Math.Max(0, settings.QueueLimit);
        _requestsPerWindow = settings.RequestsPerWindow;
        _windowSeconds = settings.WindowSeconds > 0 ? settings.WindowSeconds : 1.0;

        _concurrency = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = _concurrencyLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = _queueLimit,
        });

        if (settings.RequestsPerWindow > 0)
        {
            _rate = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = settings.RequestsPerWindow,
                TokensPerPeriod = settings.RequestsPerWindow,
                ReplenishmentPeriod = TimeSpan.FromSeconds(_windowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = _queueLimit,
                AutoReplenishment = true,
            });
        }

        _logger.LogInformation(
            "Shared throttle initialized: maxConcurrentCalls={ConcurrencyLimit}, queueLimit={QueueLimit}, requestBudget={RequestBudget}.",
            _concurrencyLimit,
            _queueLimit,
            _rate is null ? "disabled" : $"{_requestsPerWindow}/{_windowSeconds:0.##}s");
    }

    public async ValueTask<IThrottleLease> AcquireAsync(int permits = 1, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogDebug("Throttle acquire requested: {Permits} permit(s).", permits);

        // Concurrency slot first: 1 permit == 1 in-flight call, released on dispose.
        var concurrencyLease = await _concurrency.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        if (!concurrencyLease.IsAcquired)
        {
            concurrencyLease.Dispose();
            _logger.LogWarning(
                "Throttle rejected call after {WaitMs:F0} ms: concurrency queue full (limit {ConcurrencyLimit}, queueLimit {QueueLimit}).",
                stopwatch.Elapsed.TotalMilliseconds, _concurrencyLimit, _queueLimit);
            throw new ThrottleRejectedException("concurrency");
        }

        if (_rate is null)
        {
            _logger.LogDebug(
                "Throttle acquired (concurrency only) after {WaitMs:F0} ms.",
                stopwatch.Elapsed.TotalMilliseconds);
            return new CompositeLease(concurrencyLease, null);
        }

        // Then the request budget (RPM/QPS). Token-bucket permits refill on a timer, so the lease dispose
        // is a no-op there - the concurrency slot is what gets returned.
        RateLimitLease rateLease;
        try
        {
            rateLease = await _rate.AcquireAsync(Math.Max(1, permits), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            concurrencyLease.Dispose();
            _logger.LogWarning(
                ex,
                "Throttle acquire failed on the request-budget limiter after {WaitMs:F0} ms; releasing concurrency slot.",
                stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }

        if (!rateLease.IsAcquired)
        {
            rateLease.Dispose();
            concurrencyLease.Dispose();
            _logger.LogWarning(
                "Throttle rejected call after {WaitMs:F0} ms: request budget exhausted (limit {RequestsPerWindow}/{WindowSeconds:0.##}s, queueLimit {QueueLimit}).",
                stopwatch.Elapsed.TotalMilliseconds, _requestsPerWindow, _windowSeconds, _queueLimit);
            throw new ThrottleRejectedException("rate");
        }

        _logger.LogDebug(
            "Throttle acquired {Permits} permit(s) after {WaitMs:F0} ms.",
            permits, stopwatch.Elapsed.TotalMilliseconds);
        return new CompositeLease(concurrencyLease, rateLease);
    }

    public void Dispose()
    {
        _concurrency.Dispose();
        _rate?.Dispose();
    }

    /// <summary>Holds the underlying rate-limiter leases and returns them all on dispose.</summary>
    private sealed class CompositeLease : IThrottleLease
    {
        private readonly RateLimitLease _concurrencyLease;
        private readonly RateLimitLease? _rateLease;
        private bool _disposed;

        public CompositeLease(RateLimitLease concurrencyLease, RateLimitLease? rateLease)
        {
            _concurrencyLease = concurrencyLease;
            _rateLease = rateLease;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _rateLease?.Dispose();
            _concurrencyLease.Dispose();
        }
    }
}
