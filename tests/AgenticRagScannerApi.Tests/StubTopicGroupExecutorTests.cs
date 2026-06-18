using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Core.Throttling;
using AgenticRagScannerApi.Orchestration;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Story 1.2 - the Phase 1 stub returns a placeholder "Completed" result with no items and routes
/// its work through the shared throttle (the rate-limiting seam Epic 2's outbound calls will use).
/// </summary>
public class StubTopicGroupExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_RoutesThroughThrottle_AndReturnsCompletedPlaceholder()
    {
        var lease = new Mock<IThrottleLease>();
        var throttle = new Mock<ISharedThrottle>();
        throttle
            .Setup(t => t.AcquireAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<IThrottleLease>(lease.Object));

        var executor = new StubTopicGroupExecutor(
            throttle.Object,
            Mock.Of<ILogger<StubTopicGroupExecutor>>());

        var context = CreateContext("tax", "Tax");

        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        result.GroupId.Should().Be("tax");
        result.GroupName.Should().Be("Tax");
        result.Status.Should().Be("Completed");
        result.Items.Should().BeEmpty();

        throttle.Verify(t => t.AcquireAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        lease.Verify(l => l.Dispose(), Times.Once);
    }

    private static TopicGroupContext CreateContext(string id, string name) =>
        new()
        {
            Run = new RunContext
            {
                RunId = "run-1",
                Jurisdiction = "United Kingdom",
                AuthoritativeSources = [],
            },
            TopicGroup = new TopicGroup
            {
                Id = id,
                Name = name,
                Keywords = [name],
            },
        };
}
