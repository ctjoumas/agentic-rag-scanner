using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Agents;
using AgenticRagScannerApi.Workflows.Pipeline;
using AgenticRagScannerApi.Workflows.Steps;
using AgenticRagScannerApi.Workflows.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Shared builders for the Epic 2 workflow tests: contexts, a pipeline wired from the real stubs
/// (no mocks - the stubs are deterministic), and small fixture helpers.
/// </summary>
internal static class WorkflowTestFactory
{
    public static TopicGroupContext CreateContext(
        string id = "tax",
        string name = "Tax",
        int maxLoops = 3,
        IReadOnlyList<string>? allowlist = null)
    {
        var run = new RunContext
        {
            RunId = "run-1",
            Jurisdiction = "United Kingdom",
            AuthoritativeSources = allowlist ?? [],
        };

        var group = new TopicGroup
        {
            Id = id,
            Name = name,
            Keywords = [name],
            MaxLoops = maxLoops,
        };

        return new TopicGroupContext { Run = run, TopicGroup = group };
    }

    public static TopicGroupPipeline CreatePipeline() =>
        new(
            new QuerySynthesisAgentStub(NullLogger<QuerySynthesisAgentStub>.Instance),
            new BingSearchTool(NullLogger<BingSearchTool>.Instance),
            new PreFilterStep(NullLogger<PreFilterStep>.Instance),
            new FetchAndCleanStep(NullLogger<FetchAndCleanStep>.Instance),
            new RelevanceEvalAgentStub(NullLogger<RelevanceEvalAgentStub>.Instance),
            new LoopController(NullLogger<LoopController>.Instance),
            new VerdictRouting(NullLogger<VerdictRouting>.Instance),
            new EnrichmentAgentStub(NullLogger<EnrichmentAgentStub>.Instance),
            new CategorizeAgentStub(NullLogger<CategorizeAgentStub>.Instance),
            new SummarizeImpactAgentStub(NullLogger<SummarizeImpactAgentStub>.Instance),
            NullLogger<TopicGroupPipeline>.Instance);

    public static ResultItem Item(string url, Verdict verdict) =>
        new()
        {
            RunId = "run-1",
            GroupId = "tax",
            Id = url,
            SourceUrls = [url],
            Verdict = verdict,
        };

    public static FetchedDocument Doc(string url) =>
        new() { Hit = new SearchHit { Url = url, SourceQuery = "q" }, CleanedText = "cleaned text" };

    public static ReviewDecision Decision(params Verdict[] verdicts)
    {
        var items = verdicts
            .Select((verdict, index) => new ItemVerdict { Index = index, Verdict = verdict, Rationale = "r" })
            .ToList();

        return new ReviewDecision
        {
            ThoughtProcess = "stub",
            Decision = LoopDecision.Retry,
            Items = items,
        };
    }
}
