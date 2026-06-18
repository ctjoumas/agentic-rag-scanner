using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Steps;

/// <inheritdoc />
public sealed class VerdictRouting : IVerdictRouting
{
    private readonly ILogger<VerdictRouting> _logger;

    public VerdictRouting(ILogger<VerdictRouting> logger) => _logger = logger;

    public IReadOnlyList<ResultItem> Route(TopicGroupContext context)
    {
        // Vetted = Relevant/Borderline carried across all passes; Discarded = NotRelevant (audit only).
        var carried = context.History.Vetted.ToList();
        var dropped = context.History.Discarded.ToList();

        foreach (var item in dropped)
        {
            _logger.LogInformation(
                "Verdict routing: dropped NOT_RELEVANT item '{ItemId}' ({Url}) for group '{GroupId}' (logged for audit).",
                item.Id, item.SourceUrls.Count > 0 ? item.SourceUrls[0] : "(no url)", context.TopicGroup.Id);
        }

        var borderline = carried.Count(item => item.Verdict == Verdict.Borderline);

        _logger.LogInformation(
            "Verdict routing: group '{GroupId}' carrying {Carried} item(s) to enrichment ({Borderline} borderline); dropped {Dropped}.",
            context.TopicGroup.Id, carried.Count, borderline, dropped.Count);

        return carried;
    }
}
