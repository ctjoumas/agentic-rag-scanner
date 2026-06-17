namespace AgenticRagScannerApi.Core.Throttling;

/// <summary>
/// A held throttle reservation. Dispose to release the quota back to the limiter.
/// (Concurrency limits return the slot on dispose; time-based limits refill on a timer, so dispose
/// is a no-op there - callers still wrap it in <c>using</c> so the same code works for both.)
/// </summary>
public interface IThrottleLease : IDisposable
{
}
