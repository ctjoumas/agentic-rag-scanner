using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Workflows.Prompts;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows.Agents;

/// <summary>
/// Epic 2 stub for <see cref="ICategorizeAgent"/>: stamps canned impact area / regulator / tags from
/// a small placeholder vocabulary - no LLM call. The real agent (Epic 7) draws tags only from the
/// approved controlled vocabulary.
/// </summary>
public sealed class CategorizeAgentStub : ICategorizeAgent
{
    private static readonly IReadOnlyList<string> ApprovedTags = ["payroll", "reporting", "stub"];

    private readonly ILogger<CategorizeAgentStub> _logger;

    public CategorizeAgentStub(ILogger<CategorizeAgentStub> logger) => _logger = logger;

    public Task<ResultItem> CategorizeAsync(ResultItem item, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Categorize stub ({PromptVersion}) for item '{ItemId}'.",
            CategorizePrompt.Version, item.Id);

        item.ImpactArea = "General";
        item.Regulator = "Unknown";
        item.Tags = ["stub"];

        return Task.FromResult(item);
    }
}
