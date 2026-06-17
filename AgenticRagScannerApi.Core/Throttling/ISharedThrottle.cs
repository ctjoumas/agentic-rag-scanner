namespace AgenticRagScannerApi.Core.Throttling;

/// <summary>
/// Shared gate that outbound, quota-limited calls (Azure OpenAI TPM/RPM, Bing QPS) funnel through
/// so N parallel topic-group workflows don't exceed shared service limits. Phase 0 ships a
/// pass-through implementation; the real one can sit on System.Threading.RateLimiting without Core
/// taking that dependency.
/// </summary>
public interface ISharedThrottle
{
    /// <summary>
    /// Waits until <paramref name="permits"/> units of quota are available, then returns a lease;
    /// dispose it when the call finishes to release the quota. Use 1 permit per request (RPM/QPS),
    /// or an estimated token count for token-based limits (TPM).
    /// </summary>
    ValueTask<IThrottleLease> AcquireAsync(int permits = 1, CancellationToken cancellationToken = default);
}
