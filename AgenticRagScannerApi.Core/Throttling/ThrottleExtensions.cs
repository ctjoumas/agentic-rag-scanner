namespace AgenticRagScannerApi.Core.Throttling;

/// <summary>
/// Ergonomic helpers over <see cref="ISharedThrottle"/> so everyday call sites never touch a lease:
/// acquire quota, run the operation, then release.
/// </summary>
public static class ThrottleExtensions
{
    /// <summary>Acquires quota, runs <paramref name="operation"/>, returns its result, then releases.</summary>
    public static async Task<T> ExecuteAsync<T>(
        this ISharedThrottle throttle,
        Func<CancellationToken, Task<T>> operation,
        int permits = 1,
        CancellationToken cancellationToken = default)
    {
        using var lease = await throttle.AcquireAsync(permits, cancellationToken).ConfigureAwait(false);
        return await operation(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Acquires quota, runs <paramref name="operation"/>, then releases (no result).</summary>
    public static async Task ExecuteAsync(
        this ISharedThrottle throttle,
        Func<CancellationToken, Task> operation,
        int permits = 1,
        CancellationToken cancellationToken = default)
    {
        using var lease = await throttle.AcquireAsync(permits, cancellationToken).ConfigureAwait(false);
        await operation(cancellationToken).ConfigureAwait(false);
    }
}
