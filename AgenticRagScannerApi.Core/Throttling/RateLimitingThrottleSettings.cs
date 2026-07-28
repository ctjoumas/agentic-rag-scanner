namespace AgenticRagScannerApi.Core.Throttling;

/// <summary>
/// Immutable settings that shape a <see cref="RateLimitingThrottle"/>. Kept as a Core-local POCO so the
/// throttle takes no dependency on the API's options binding; the API constructs this from its bound
/// <c>ThrottleOptions</c> configuration section.
/// </summary>
/// <param name="MaxConcurrentCalls">
/// Maximum number of outbound, quota-limited calls (Azure OpenAI / Bing) allowed in flight at once across
/// all topic groups. This is the call-level concurrency cap - distinct from the orchestrator's per-group
/// worker cap - so a real service connection limit is never exceeded no matter how many groups run.
/// </param>
/// <param name="RequestsPerWindow">
/// Token-bucket capacity: the number of request permits replenished each <paramref name="WindowSeconds"/>
/// window (the RPM/QPS budget). Set to <c>0</c> to disable rate limiting and only cap concurrency.
/// </param>
/// <param name="WindowSeconds">Replenishment period, in seconds, for the request token bucket.</param>
/// <param name="QueueLimit">
/// How many callers may wait for a permit once the limit is hit. Waiting (backpressure) is preferred over
/// rejection; when the queue is full the throttle throws <see cref="ThrottleRejectedException"/> so the
/// caller's resilience pipeline can back off.
/// </param>
public sealed record RateLimitingThrottleSettings(
    int MaxConcurrentCalls,
    int RequestsPerWindow,
    double WindowSeconds,
    int QueueLimit);
