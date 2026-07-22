using System.ComponentModel.DataAnnotations;
using AgenticRagScannerApi.Core.Throttling;

namespace AgenticRagScannerApi.Configuration;

/// <summary>
/// Binds to the "Throttle" configuration section. Tunes the shared outbound throttle
/// (<see cref="RateLimitingThrottle"/>) that gates Azure OpenAI / Bing calls, plus the orchestrator's
/// cap on how many topic-group workflows run in parallel (Epic 13). These two dimensions are separate:
/// <see cref="MaxParallelTopicGroups"/> caps concurrent <em>groups</em>, while <see cref="MaxConcurrentCalls"/>
/// and the token-bucket settings cap concurrent <em>outbound calls</em> across all groups.
/// </summary>
public sealed class ThrottleOptions
{
    public const string SectionName = "Throttle";

    /// <summary>
    /// Maximum topic-group workflows executed concurrently within a single scan run. Bounds fan-out so a
    /// large request does not launch an unbounded number of parallel workflows. Default 4.
    /// </summary>
    [Range(1, 128)]
    public int MaxParallelTopicGroups { get; set; } = 4;

    /// <summary>
    /// Maximum outbound, quota-limited calls (Azure OpenAI / Bing) in flight at once across all groups.
    /// Keep at or below the real service connection/concurrency limit. Default 8.
    /// </summary>
    [Range(1, 1024)]
    public int MaxConcurrentCalls { get; set; } = 8;

    /// <summary>
    /// Request budget replenished each <see cref="WindowSeconds"/> window (the RPM/QPS ceiling). Set to 0
    /// to disable rate limiting and cap concurrency only. Default 60.
    /// </summary>
    [Range(0, 100000)]
    public int RequestsPerWindow { get; set; } = 60;

    /// <summary>Replenishment window, in seconds, for <see cref="RequestsPerWindow"/>. Default 60.</summary>
    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// How many callers may wait for a permit once a limit is hit (backpressure). Beyond this the throttle
    /// rejects with <see cref="ThrottleRejectedException"/> so the caller's resilience pipeline backs off.
    /// Default 256.
    /// </summary>
    [Range(0, 100000)]
    public int QueueLimit { get; set; } = 256;

    /// <summary>Projects these options onto the Core-local settings the throttle consumes.</summary>
    public RateLimitingThrottleSettings ToThrottleSettings() =>
        new(MaxConcurrentCalls, RequestsPerWindow, WindowSeconds, QueueLimit);
}
