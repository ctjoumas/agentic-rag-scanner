namespace AgenticRagScannerApi.Core.Throttling;

/// <summary>
/// Phase 0 pass-through throttle: grants every request immediately, with no limiting.
/// Lets call sites be "throttle-ready" before the real TPM/RPM/QPS limiter is wired in later.
/// </summary>
public sealed class NoOpThrottle : ISharedThrottle
{
    public ValueTask<IThrottleLease> AcquireAsync(int permits = 1, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IThrottleLease>(NoOpLease.Instance);

    private sealed class NoOpLease : IThrottleLease
    {
        public static readonly NoOpLease Instance = new();

        public void Dispose() { }
    }
}
