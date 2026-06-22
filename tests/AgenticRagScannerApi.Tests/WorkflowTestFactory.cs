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
            new FakeWebSearchAgent(),
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

/// <summary>
/// Deterministic <see cref="IWebSearchAgent"/> test double for the workflow pipeline tests. Returns a
/// fixed set of canned, allowlist-scoped hits per query (no network), replacing the former production
/// <c>BingSearchTool</c> stub that Epic 4 removed.
/// </summary>
internal sealed class FakeWebSearchAgent : IWebSearchAgent
{
    private const string DefaultAllowlistHost = "www.gov.uk";

    public Task<IReadOnlyList<SearchHit>> SearchAsync(string query, RunContext run, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> allowlist = run.AuthoritativeSources.Count > 0
            ? run.AuthoritativeSources
            : [DefaultAllowlistHost];

        var slug = new string(query.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (slug.Length == 0)
        {
            slug = "q";
        }

        var hits = new List<SearchHit>(3);
        for (var i = 0; i < 3; i++)
        {
            var source = allowlist[i % allowlist.Count];
            var host = Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri.Host : source.Trim('/');
            hits.Add(new SearchHit
            {
                Url = $"https://{host}/canned/{slug}/{i}",
                Title = $"Canned result {i + 1} for '{query}'",
                Snippet = $"Canned snippet for '{query}'.",
                Domain = host,
                SourceQuery = query,
                Rank = i + 1,
            });
        }

        return Task.FromResult<IReadOnlyList<SearchHit>>(hits);
    }
}
