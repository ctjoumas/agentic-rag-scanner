using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Models;
using AgenticRagScannerApi.Orchestration;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Story 1.1 - the orchestrator maps a request to one context per topic group (each seeded with an
/// empty SearchHistory and sharing one RunContext), runs the groups sequentially in request order,
/// and aggregates their results.
/// </summary>
public class ScanOrchestratorTests
{
    [Fact]
    public async Task RunAsync_CreatesOneContextPerGroup_SeededWithEmptyHistory_SharingOneRunContext()
    {
        var captured = new List<TopicGroupContext>();
        var orchestrator = new ScanOrchestrator(
            CreateCapturingExecutor(captured),
            Mock.Of<ILogger<ScanOrchestrator>>());

        var asOfDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new ScanRequest
        {
            AsOfDate = asOfDate,
            Jurisdiction = "United Kingdom",
            TopicGroups = ["Tax", "Conduct", "Capital"],
        };

        var result = await orchestrator.RunAsync(request, CancellationToken.None);

        captured.Should().HaveCount(3);
        captured.Should().OnlyContain(c => c.History.Passes.Count == 0);
        captured.Should().OnlyContain(c => c.Run.RunId == result.RunId);
        captured.Should().OnlyContain(c => c.Run.Jurisdiction == "United Kingdom");
        captured.Should().OnlyContain(c => c.Run.AsOfDate == asOfDate);
        captured.Should().OnlyContain(c =>
            c.TopicGroup.Keywords.Count == 1 && c.TopicGroup.Keywords[0] == c.TopicGroup.Name);
    }

    [Fact]
    public async Task RunAsync_ExecutesGroupsSequentiallyInRequestOrder_AndAggregatesResults()
    {
        var captured = new List<TopicGroupContext>();
        var orchestrator = new ScanOrchestrator(
            CreateCapturingExecutor(captured),
            Mock.Of<ILogger<ScanOrchestrator>>());

        var request = new ScanRequest
        {
            AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Jurisdiction = "United Kingdom",
            TopicGroups = ["Tax", "Conduct", "Capital"],
        };

        var result = await orchestrator.RunAsync(request, CancellationToken.None);

        captured.Select(c => c.TopicGroup.Name).Should().Equal("Tax", "Conduct", "Capital");
        result.Groups.Select(g => g.GroupName).Should().Equal("Tax", "Conduct", "Capital");
        result.Groups.Should().OnlyContain(g => g.Status == "Completed");
        result.RunId.Should().NotBeNullOrWhiteSpace();
        result.CompletedAtUtc.Should().BeOnOrAfter(result.StartedAtUtc);
    }

    [Fact]
    public async Task RunAsync_SplitsCommaSeparatedGroup_IntoOneContextWithKeywordOrList()
    {
        var captured = new List<TopicGroupContext>();
        var orchestrator = new ScanOrchestrator(
            CreateCapturingExecutor(captured),
            Mock.Of<ILogger<ScanOrchestrator>>());

        var request = new ScanRequest
        {
            AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Jurisdiction = "United Kingdom",
            // One topic group expressed as a comma-separated list: extra whitespace and a
            // case-insensitive duplicate ("Employee NIC") should be normalized away.
            TopicGroups = ["Employee NIC,  Income Tax , ITEPA 2003 , employee nic"],
        };

        await orchestrator.RunAsync(request, CancellationToken.None);

        captured.Should().HaveCount(1);
        var group = captured[0].TopicGroup;
        group.Keywords.Should().Equal("Employee NIC", "Income Tax", "ITEPA 2003");
        group.Name.Should().Be("Employee NIC, Income Tax, ITEPA 2003");
        group.Id.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RunAsync_WithNoTopicGroups_ReturnsEmptyAggregate_WithoutCallingExecutor()
    {
        var executor = new Mock<ITopicGroupExecutor>();
        var orchestrator = new ScanOrchestrator(executor.Object, Mock.Of<ILogger<ScanOrchestrator>>());

        var request = new ScanRequest
        {
            Jurisdiction = "United Kingdom",
            TopicGroups = [],
        };

        var result = await orchestrator.RunAsync(request, CancellationToken.None);

        result.Groups.Should().BeEmpty();
        result.RunId.Should().NotBeNullOrWhiteSpace();
        executor.Verify(
            e => e.ExecuteAsync(It.IsAny<TopicGroupContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static ITopicGroupExecutor CreateCapturingExecutor(List<TopicGroupContext> captured)
    {
        var executor = new Mock<ITopicGroupExecutor>();
        executor
            .Setup(e => e.ExecuteAsync(It.IsAny<TopicGroupContext>(), It.IsAny<CancellationToken>()))
            .Returns((TopicGroupContext context, CancellationToken _) =>
            {
                captured.Add(context);
                return Task.FromResult(new TopicGroupResult
                {
                    GroupId = context.TopicGroup.Id,
                    GroupName = context.TopicGroup.Name,
                    Status = "Completed",
                });
            });
        return executor.Object;
    }
}
