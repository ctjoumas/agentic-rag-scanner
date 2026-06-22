using System.Text.RegularExpressions;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Models;

namespace AgenticRagScannerApi.Orchestration;

/// <summary>
/// Synchronous, sequential scan orchestrator (story 1.1). Maps the request to one
/// <see cref="TopicGroupContext"/> per topic group (each seeded with an empty
/// <see cref="SearchHistory"/>), runs the groups one at a time through the per-group executor, and
/// aggregates their results. Parallel fan-out under the shared throttle is deferred to Epic 12.
/// </summary>
public sealed class ScanOrchestrator : IScanOrchestrator
{
    private readonly ITopicGroupExecutor _executor;
    private readonly ILogger<ScanOrchestrator> _logger;

    public ScanOrchestrator(ITopicGroupExecutor executor, ILogger<ScanOrchestrator> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    public async Task<ScanResult> RunAsync(ScanRequest request, CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("N");
        var startedAtUtc = DateTimeOffset.UtcNow;

        var run = new RunContext
        {
            RunId = runId,
            Jurisdiction = request.Jurisdiction,
            AsOfDate = request.AsOfDate,
            // The primary-source allowlist is enforced by the Web Search agent's Bing Custom Search
            // configuration (Epic 4). RunContext.AuthoritativeSources stays empty here; when populated it
            // adds a client-side, defense-in-depth host filter on top of that hosted scoping.
            AuthoritativeSources = [],
            StartedAtUtc = startedAtUtc,
        };

        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["runId"] = runId });

        _logger.LogInformation(
            "Scan run starting: jurisdiction={Jurisdiction}, asOfDate={AsOfDate}, topicGroups={TopicGroupCount}.",
            request.Jurisdiction, request.AsOfDate, request.TopicGroups.Count);

        var contexts = MapToContexts(run, request.TopicGroups);
        var results = new List<TopicGroupResult>(contexts.Count);

        // Sequential execution - one group at a time. Parallel fan-out is deferred to Epic 12.
        foreach (var context in contexts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _executor.ExecuteAsync(context, cancellationToken);
            results.Add(result);
        }

        var completedAtUtc = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Scan run completed: topicGroups={TopicGroupCount}, durationMs={DurationMs}.",
            results.Count, (completedAtUtc - startedAtUtc).TotalMilliseconds);

        return new ScanResult
        {
            RunId = runId,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            Groups = results,
        };
    }

    private static IReadOnlyList<TopicGroupContext> MapToContexts(RunContext run, IReadOnlyList<string> topicGroups)
    {
        var contexts = new List<TopicGroupContext>(topicGroups.Count);

        foreach (var name in topicGroups)
        {
            var group = new TopicGroup
            {
                Id = ToGroupId(name),
                Name = name,
                // POC: each selected group name becomes a single-phrase OR-list until curated
                // keyword/synonym groups arrive (Epic 2/3).
                Keywords = [name],
            };

            // TopicGroupContext seeds an empty SearchHistory on construction.
            contexts.Add(new TopicGroupContext { Run = run, TopicGroup = group });
        }

        return contexts;
    }

    /// <summary>Derives a stable, log-friendly slug id from a topic group name.</summary>
    private static string ToGroupId(string name)
    {
        var slug = Regex.Replace(name.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? Guid.NewGuid().ToString("N") : slug;
    }
}