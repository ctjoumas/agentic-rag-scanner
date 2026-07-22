using AgenticRagScannerApi.Core.Throttling;
using FluentAssertions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Epic 13 - the real shared throttle caps concurrent outbound calls (ConcurrencyLimiter) and enforces a
/// per-window request budget (TokenBucketRateLimiter). With a zero-length wait queue, exceeding either
/// limit fails fast with <see cref="ThrottleRejectedException"/> so callers can back off; releasing a
/// concurrency lease frees the slot for reuse.
/// </summary>
public class RateLimitingThrottleTests
{
    [Fact]
    public async Task AcquireAsync_CapsConcurrentCalls_AndRejectsBeyondQueue()
    {
        // 2 concurrent calls, no rate limit, no wait queue.
        using var throttle = new RateLimitingThrottle(new RateLimitingThrottleSettings(
            MaxConcurrentCalls: 2, RequestsPerWindow: 0, WindowSeconds: 60, QueueLimit: 0));

        var lease1 = await throttle.AcquireAsync();
        var lease2 = await throttle.AcquireAsync();

        // Both slots held; the queue is empty, so a third acquire is rejected immediately.
        var act = async () => await throttle.AcquireAsync();
        await act.Should().ThrowAsync<ThrottleRejectedException>();

        lease1.Dispose();
        lease2.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_ReleasedSlot_IsReusable()
    {
        using var throttle = new RateLimitingThrottle(new RateLimitingThrottleSettings(
            MaxConcurrentCalls: 1, RequestsPerWindow: 0, WindowSeconds: 60, QueueLimit: 0));

        var lease1 = await throttle.AcquireAsync();
        lease1.Dispose();

        // The single slot is free again, so this must succeed.
        var lease2 = await throttle.AcquireAsync();
        lease2.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_EnforcesRequestBudget()
    {
        // Plenty of concurrency, but only 2 requests per window and no wait queue.
        using var throttle = new RateLimitingThrottle(new RateLimitingThrottleSettings(
            MaxConcurrentCalls: 10, RequestsPerWindow: 2, WindowSeconds: 60, QueueLimit: 0));

        // Releasing the lease returns the concurrency slot but does not refill the token bucket.
        (await throttle.AcquireAsync()).Dispose();
        (await throttle.AcquireAsync()).Dispose();

        // Budget exhausted for this window -> rejected.
        var act = async () => await throttle.AcquireAsync();
        await act.Should().ThrowAsync<ThrottleRejectedException>();
    }

    [Fact]
    public async Task AcquireAsync_NoRateLimit_AllowsRepeatedSequentialCalls()
    {
        using var throttle = new RateLimitingThrottle(new RateLimitingThrottleSettings(
            MaxConcurrentCalls: 1, RequestsPerWindow: 0, WindowSeconds: 60, QueueLimit: 0));

        for (var i = 0; i < 5; i++)
        {
            (await throttle.AcquireAsync()).Dispose();
        }
    }
}
