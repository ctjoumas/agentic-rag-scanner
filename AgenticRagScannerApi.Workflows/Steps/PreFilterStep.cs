using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Steps;

/// <inheritdoc />
public sealed class PreFilterStep : IPreFilterStep
{
    private readonly ILogger<PreFilterStep> _logger;

    public PreFilterStep(ILogger<PreFilterStep> logger) => _logger = logger;

    public IReadOnlyList<SearchHit> Filter(IReadOnlyList<SearchHit> hits, TopicGroupContext context)
    {
        var kept = new List<SearchHit>(hits.Count);
        var invalid = 0;
        var dupeGroup = 0;
        var dupeCrossGroup = 0;

        foreach (var hit in hits)
        {
            if (!UrlCanonicalizer.TryCanonicalize(hit.Url, out var key))
            {
                invalid++;
                continue; // invalid / non-http(s) URL dropped
            }

            // Per-group dedupe (checkpoint-backed; also covers earlier passes in this group).
            if (!context.History.ProcessedKeys.Add(key))
            {
                dupeGroup++;
                continue;
            }

            // Cross-group dedupe (run-level): another group already surfaced this URL this run.
            if (!context.Run.SeenUrlKeys.TryAdd(key, 0))
            {
                dupeCrossGroup++;
                continue;
            }

            kept.Add(hit);
        }

        _logger.LogDebug(
            "Pre-filter (group '{GroupId}'): {InCount} hit(s) -> {OutCount} kept " +
            "({Invalid} invalid, {DupeGroup} in-group dupe, {DupeCrossGroup} cross-group dupe).",
            context.TopicGroup.Id, hits.Count, kept.Count, invalid, dupeGroup, dupeCrossGroup);

        return kept;
    }
}
