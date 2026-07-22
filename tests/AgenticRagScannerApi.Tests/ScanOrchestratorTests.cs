using AgenticRagScannerApi.Configuration;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Models;
using AgenticRagScannerApi.Orchestration;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Story 1.1 + Epic 13.1 - the orchestrator maps a request to one context per topic group (each seeded
/// with an empty SearchHistory and sharing one RunContext), runs the groups in parallel under a worker
/// cap, aggregates their results in request order, and isolates per-group failures.
/// </summary>
public class ScanOrchestratorTests
{
    [Fact]
    public async Task RunAsync_CreatesOneContextPerGroup_SeededWithEmptyHistory_SharingOneRunContext()
    {
        var captured = new CapturedContexts();
        var orchestrator = CreateOrchestrator(CreateCapturingExecutor(captured));

        var startDate = new DateOnly(2026, 1, 1);
        var endDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new ScanRequest
        {
            StartDate = startDate,
            EndDate = endDate,
            Jurisdiction = "United Kingdom",
            TopicGroups = ["Tax", "Conduct", "Capital"],
        };

        var result = await orchestrator.RunAsync(request, CancellationToken.None);

        var contexts = captured.Snapshot();
        contexts.Should().HaveCount(3);
        contexts.Should().OnlyContain(c => c.History.Passes.Count == 0);
        contexts.Should().OnlyContain(c => c.Run.RunId == result.RunId);
        contexts.Should().OnlyContain(c => c.Run.Jurisdiction == "United Kingdom");
        contexts.Should().OnlyContain(c => c.Run.StartDate == startDate);
        contexts.Should().OnlyContain(c => c.Run.EndDate == endDate);
        contexts.Should().OnlyContain(c =>
            c.TopicGroup.Keywords.Count == 1 && c.TopicGroup.Keywords[0] == c.TopicGroup.Name);
    }

    [Fact]
    public async Task RunAsync_RunsGroupsInParallel_AndAggregatesResultsInRequestOrder()
    {
        var captured = new CapturedContexts();
        var orchestrator = CreateOrchestrator(CreateCapturingExecutor(captured));

        var request = new ScanRequest
        {
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Jurisdiction = "United Kingdom",
            TopicGroups = ["Tax", "Conduct", "Capital"],
        };

        var result = await orchestrator.RunAsync(request, CancellationToken.None);

        // All three groups ran (execution order is nondeterministic under parallelism)...
        captured.Snapshot().Select(c => c.TopicGroup.Name).Should().BeEquivalentTo("Tax", "Conduct", "Capital");
        // ...but the aggregated results preserve the request order.
        result.Groups.Select(g => g.GroupName).Should().Equal("Tax", "Conduct", "Capital");
        result.Groups.Should().OnlyContain(g => g.Status == "Completed");
        result.RunId.Should().NotBeNullOrWhiteSpace();
        result.CompletedAtUtc.Should().BeOnOrAfter(result.StartedAtUtc);
    }

    [Fact]
    public async Task RunAsync_IsolatesGroupFailure_OtherGroupsComplete_FailedGroupMarkedFailed()
    {
        // The executor throws for "Conduct" and completes the others.
        var executor = new Mock<ITopicGroupExecutor>();
        executor
            .Setup(e => e.ExecuteAsync(It.IsAny<TopicGroupContext>(), It.IsAny<CancellationToken>()))
            .Returns((TopicGroupContext context, CancellationToken _) =>
            {
                if (context.TopicGroup.Name == "Conduct")
                {
                    throw new InvalidOperationException("boom");
                }

                return Task.FromResult(new TopicGroupResult
                {
                    GroupId = context.TopicGroup.Id,
                    GroupName = context.TopicGroup.Name,
                    Status = "Completed",
                });
            });

        var orchestrator = CreateOrchestrator(executor.Object);
        var request = new ScanRequest
        {
            Jurisdiction = "United Kingdom",
            TopicGroups = ["Tax", "Conduct", "Capital"],
        };

        var result = await orchestrator.RunAsync(request, CancellationToken.None);

        result.Groups.Select(g => g.GroupName).Should().Equal("Tax", "Conduct", "Capital");
        result.Groups.Single(g => g.GroupName == "Tax").Status.Should().Be("Completed");
        result.Groups.Single(g => g.GroupName == "Capital").Status.Should().Be("Completed");
        result.Groups.Single(g => g.GroupName == "Conduct").Status.Should().Be("Failed");
    }

    [Fact]
    public async Task RunAsync_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var executor = new Mock<ITopicGroupExecutor>();
        executor
            .Setup(e => e.ExecuteAsync(It.IsAny<TopicGroupContext>(), It.IsAny<CancellationToken>()))
            .Returns((TopicGroupContext _, CancellationToken token) =>
            {
                cts.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new TopicGroupResult
                {
                    GroupId = "x",
                    GroupName = "x",
                    Status = "Completed",
                });
            });

        var orchestrator = CreateOrchestrator(executor.Object);
        var request = new ScanRequest
        {
            Jurisdiction = "United Kingdom",
            TopicGroups = ["Tax", "Conduct"],
        };

        var act = () => orchestrator.RunAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunAsync_DuplicateTopicGroups_GetDistinctGroupIds()
    {
        var captured = new CapturedContexts();
        var orchestrator = CreateOrchestrator(CreateCapturingExecutor(captured));

        var request = new ScanRequest
        {
            Jurisdiction = "United Kingdom",
            // Two identical topic groups - they hash to the same base id and must be disambiguated so
            // parallel Cosmos checkpoints do not collide.
            TopicGroups = ["Employee NIC, Income Tax", "Employee NIC, Income Tax"],
        };

        var result = await orchestrator.RunAsync(request, CancellationToken.None);

        result.Groups.Should().HaveCount(2);
        result.Groups.Select(g => g.GroupId).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task RunAsync_SplitsCommaSeparatedGroup_IntoOneContextWithKeywordOrList()
    {
        var captured = new CapturedContexts();
        var orchestrator = CreateOrchestrator(CreateCapturingExecutor(captured));

        var request = new ScanRequest
        {
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Jurisdiction = "United Kingdom",
            // One topic group expressed as a comma-separated list: extra whitespace and a
            // case-insensitive duplicate ("Employee NIC") should be normalized away.
            TopicGroups = ["Employee NIC,  Income Tax , ITEPA 2003 , employee nic"],
        };

        await orchestrator.RunAsync(request, CancellationToken.None);

        var contexts = captured.Snapshot();
        contexts.Should().HaveCount(1);
        var group = contexts[0].TopicGroup;
        group.Keywords.Should().Equal("Employee NIC", "Income Tax", "ITEPA 2003");
        group.Name.Should().Be("Employee NIC, Income Tax, ITEPA 2003");
        group.Id.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RunAsync_WithNoTopicGroups_ReturnsEmptyAggregate_WithoutCallingExecutor()
    {
        var executor = new Mock<ITopicGroupExecutor>();
        var orchestrator = CreateOrchestrator(executor.Object);

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

    private static ScanOrchestrator CreateOrchestrator(ITopicGroupExecutor executor, int maxParallel = 4)
    {
        var options = Options.Create(new ThrottleOptions { MaxParallelTopicGroups = maxParallel });
        return new ScanOrchestrator(executor, options, Mock.Of<ILogger<ScanOrchestrator>>());
    }

    private static ITopicGroupExecutor CreateCapturingExecutor(CapturedContexts captured)
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

    /// <summary>Thread-safe capture of the contexts the executor received (groups run in parallel).</summary>
    private sealed class CapturedContexts
    {
        private readonly object _gate = new();
        private readonly List<TopicGroupContext> _contexts = [];

        public void Add(TopicGroupContext context)
        {
            lock (_gate)
            {
                _contexts.Add(context);
            }
        }

        public IReadOnlyList<TopicGroupContext> Snapshot()
        {
            lock (_gate)
            {
                return _contexts.ToList();
            }
        }
    }
}
