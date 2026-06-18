using AgenticRagScannerApi.Core.Throttling;
using FluentAssertions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Phase 0 throttle is a pass-through, so these just verify the abstraction's plumbing:
/// AcquireAsync hands back a disposable lease, and ExecuteAsync runs the operation and returns its result.
/// </summary>
public class NoOpThrottleTests
{
    [Fact]
    public async Task AcquireAsync_ShouldReturnADisposableLease()
    {
        var throttle = new NoOpThrottle();

        using var lease = await throttle.AcquireAsync();

        lease.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRunTheOperationAndReturnItsResult()
    {
        var throttle = new NoOpThrottle();
        var ran = false;

        var result = await throttle.ExecuteAsync(_ =>
        {
            ran = true;
            return Task.FromResult(42);
        });

        ran.Should().BeTrue();
        result.Should().Be(42);
    }

    [Fact]
    public async Task ExecuteAsync_VoidOverload_ShouldRunTheOperation()
    {
        var throttle = new NoOpThrottle();
        var ran = false;

        await throttle.ExecuteAsync(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        ran.Should().BeTrue();
    }
}
