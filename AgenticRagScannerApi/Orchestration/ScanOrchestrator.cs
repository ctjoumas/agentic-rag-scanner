using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AgenticRagScannerApi.Configuration;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Models;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Orchestration;

/// <summary>
/// Synchronous scan orchestrator (story 1.1) with parallel topic-group fan-out (Epic 13, story 13.1).
/// Maps the request to one <see cref="TopicGroupContext"/> per topic group (each seeded with an empty
/// <see cref="SearchHistory"/>), then runs the groups concurrently - capped at
/// <see cref="ThrottleOptions.MaxParallelTopicGroups"/> active workers - and aggregates their results.
/// Each group is isolated: one group faulting is surfaced as a Failed <see cref="TopicGroupResult"/> and
/// does not abort the run. Outbound LLM/Bing calls inside each workflow stay gated by the shared throttle
/// (RPM/QPS + call concurrency), which is a separate dimension from this per-group worker cap.
/// </summary>
public sealed class ScanOrchestrator : IScanOrchestrator
{
    private const string FailedStatus = "Failed";

    private readonly ITopicGroupExecutor _executor;
    private readonly ThrottleOptions _throttleOptions;
    private readonly ILogger<ScanOrchestrator> _logger;

    public ScanOrchestrator(
        ITopicGroupExecutor executor,
        IOptions<ThrottleOptions> throttleOptions,
        ILogger<ScanOrchestrator> logger)
    {
        _executor = executor;
        _throttleOptions = throttleOptions.Value;
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
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            // The primary-source allowlist is enforced by the Web Search agent's Bing Custom Search
            // configuration (Epic 4). RunContext.AuthoritativeSources stays empty here; when populated it
            // adds a client-side, defense-in-depth host filter on top of that hosted scoping.
            AuthoritativeSources = [],
            StartedAtUtc = startedAtUtc,
        };

        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["runId"] = runId });

        _logger.LogInformation(
            "Scan run starting: jurisdiction={Jurisdiction}, startDate={StartDate}, endDate={EndDate}, topicGroups={TopicGroupCount}.",
            request.Jurisdiction, request.StartDate, request.EndDate, request.TopicGroups.Count);

        var topicGroups = MapToContexts(run, request.TopicGroups);

        // Fan out: run topic groups concurrently, capped at MaxParallelTopicGroups active workers. Results
        // are placed back at their source index so the response order matches the request regardless of
        // completion order. Each group is isolated - a fault becomes a Failed result, never an aborted run.
        var maxParallel = Math.Max(1, _throttleOptions.MaxParallelTopicGroups);
        using var workerGate = new SemaphoreSlim(maxParallel, maxParallel);
        var results = new TopicGroupResult[topicGroups.Count];

        _logger.LogInformation(
            "Executing {TopicGroupCount} topic group(s) with up to {MaxParallel} in parallel.",
            topicGroups.Count, maxParallel);

        var tasks = new List<Task>(topicGroups.Count);
        for (var i = 0; i < topicGroups.Count; i++)
        {
            var index = i;
            var topicGroup = topicGroups[index];
            tasks.Add(Task.Run(async () =>
            {
                _logger.LogDebug(
                    "Topic group '{TopicGroupId}' ({TopicGroupName}) waiting for a worker slot ({MaxParallel} max).",
                    topicGroup.TopicGroup.Id, topicGroup.TopicGroup.Name, maxParallel);

                var gateStopwatch = Stopwatch.StartNew();
                await workerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                gateStopwatch.Stop();

                var groupStopwatch = Stopwatch.StartNew();
                try
                {
                    _logger.LogInformation(
                        "Topic group '{TopicGroupId}' ({TopicGroupName}) started after waiting {GateWaitMs:F0} ms for a worker slot.",
                        topicGroup.TopicGroup.Id, topicGroup.TopicGroup.Name, gateStopwatch.Elapsed.TotalMilliseconds);

                    results[index] = await ExecuteGroupAsync(topicGroup, cancellationToken).ConfigureAwait(false);

                    groupStopwatch.Stop();
                    _logger.LogInformation(
                        "Topic group '{TopicGroupId}' ({TopicGroupName}) finished with status {Status} in {DurationMs:F0} ms.",
                        topicGroup.TopicGroup.Id, topicGroup.TopicGroup.Name, results[index].Status, groupStopwatch.Elapsed.TotalMilliseconds);
                }
                finally
                {
                    workerGate.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var completedAtUtc = DateTimeOffset.UtcNow;

        var failedCount = results.Count(r => string.Equals(r.Status, FailedStatus, StringComparison.OrdinalIgnoreCase));
        _logger.LogInformation(
            "Scan run completed: topicGroups={TopicGroupCount}, failed={FailedCount}, durationMs={DurationMs}.",
            results.Length, failedCount, (completedAtUtc - startedAtUtc).TotalMilliseconds);

        return new ScanResult
        {
            RunId = runId,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            Groups = results,
        };
    }

    /// <summary>
    /// Runs one topic group, isolating failures: cancellation propagates (aborts the run so checkpoints can
    /// resume), but any other exception is caught and surfaced as a Failed result so sibling groups still
    /// complete and return their results.
    /// </summary>
    private async Task<TopicGroupResult> ExecuteGroupAsync(TopicGroupContext topicGroup, CancellationToken cancellationToken)
    {
        try
        {
            return await _executor.ExecuteAsync(topicGroup, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Topic group '{TopicGroupId}' ({TopicGroupName}) failed; other groups continue.",
                topicGroup.TopicGroup.Id, topicGroup.TopicGroup.Name);

            return new TopicGroupResult
            {
                GroupId = topicGroup.TopicGroup.Id,
                GroupName = topicGroup.TopicGroup.Name,
                Status = FailedStatus,
            };
        }
    }

    private static IReadOnlyList<TopicGroupContext> MapToContexts(RunContext run, IReadOnlyList<string> topicGroups)
    {
        var contexts = new List<TopicGroupContext>(topicGroups.Count);

        // Tracks how many times each base group id has been seen so identical topic groups (which hash to
        // the same id) get distinct ids. Under parallel execution the id is the Cosmos checkpoint partition
        // key, so a collision would let two concurrent workflows overwrite each other's checkpoints.
        var idCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var topicGroup in topicGroups)
        {
            // Each request entry is one topic group expressed as a comma-separated list of
            // keyword/synonym phrases. Split it into the keyword list so the whole group is
            // processed as a single unit (one synthesized query per loop pass), not one topic at a time.
            var keywords = SplitKeywords(topicGroup);
            if (keywords.Count == 0)
            {
                continue;
            }

            var name = string.Join(", ", keywords);
            var baseId = ToGroupId(keywords);
            var occurrence = idCounts.TryGetValue(baseId, out var count) ? count : 0;
            idCounts[baseId] = occurrence + 1;

            // First occurrence keeps the stable, resume-friendly id; duplicates get a "-2", "-3", ... suffix.
            var id = occurrence == 0 ? baseId : $"{baseId}-{occurrence + 1}";

            var group = new TopicGroup
            {
                Id = id,
                Name = name,
                Keywords = keywords,
            };

            // TopicGroupContext seeds an empty SearchHistory on construction.
            contexts.Add(new TopicGroupContext { Run = run, TopicGroup = group });
        }

        return contexts;
    }

    /// <summary>
    /// Splits a comma-separated topic group into its keyword list: trims each phrase, drops blanks,
    /// and removes case-insensitive duplicates while preserving first-seen order.
    /// </summary>
    private static IReadOnlyList<string> SplitKeywords(string topicGroup)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keywords = new List<string>();

        foreach (var part in topicGroup.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (seen.Add(part))
            {
                keywords.Add(part);
            }
        }

        return keywords;
    }

    /// <summary>
    /// Derives a short, stable, log-friendly id for a topic group: a slug of the first keyword plus a
    /// deterministic hash of the full (order-independent) keyword set. The hash keeps ids unique and
    /// resume-stable across runs without slugging the entire comma-separated list into the id.
    /// </summary>
    private static string ToGroupId(IReadOnlyList<string> keywords)
    {
        var slug = Regex.Replace(keywords[0].ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length == 0)
        {
            slug = "group";
        }

        var normalized = string.Join(
            '\n',
            keywords.Select(k => k.ToLowerInvariant()).OrderBy(k => k, StringComparer.Ordinal));
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var shortHash = Convert.ToHexString(hashBytes, 0, 4).ToLowerInvariant();

        return $"{slug}-{shortHash}";
    }
}