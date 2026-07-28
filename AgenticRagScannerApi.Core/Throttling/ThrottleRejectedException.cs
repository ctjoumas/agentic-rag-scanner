namespace AgenticRagScannerApi.Core.Throttling;

/// <summary>
/// Thrown when the shared throttle cannot admit a call because the wait queue is already full (the service
/// is saturated beyond the configured backpressure allowance). Callers' resilience pipelines treat it as a
/// transient, retryable condition.
/// </summary>
public sealed class ThrottleRejectedException : Exception
{
    public ThrottleRejectedException(string limiter)
        : base($"Shared throttle rejected the call: the '{limiter}' limiter queue is full.")
    {
        Limiter = limiter;
    }

    /// <summary>Which limiter rejected the call ("concurrency" or "rate").</summary>
    public string Limiter { get; }
}
