using System.Security.Cryptography;
using System.Text;
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
            var group = new TopicGroup
            {
                Id = ToGroupId(keywords),
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