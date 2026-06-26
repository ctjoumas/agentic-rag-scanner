using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Agents;
using AgenticRagScannerApi.Workflows.Configuration;
using AgenticRagScannerApi.Workflows.Pipeline;
using AgenticRagScannerApi.Workflows.Steps;
using AgenticRagScannerApi.Workflows.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;

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
            new FetchAndCleanStep(
                new StubHttpClientFactory(),
                Options.Create(new FetchOptions()),
                NullLogger<FetchAndCleanStep>.Instance),
            new RelevanceEvalAgentStub(NullLogger<RelevanceEvalAgentStub>.Instance),
            new LoopController(new StubFullTextStore(), NullLogger<LoopController>.Instance),
            new VerdictRouting(NullLogger<VerdictRouting>.Instance),
            new EnrichmentAgentStub(NullLogger<EnrichmentAgentStub>.Instance),
            new CategorizeAgentStub(NullLogger<CategorizeAgentStub>.Instance),
            new SummarizeImpactAgentStub(NullLogger<SummarizeImpactAgentStub>.Instance),
            NullLogger<TopicGroupPipeline>.Instance);

    /// <summary>
    /// Builds a service provider with the 10 deterministic step/agent stubs (by interface) plus
    /// logging, so <see cref="TopicGroupWorkflow.Build"/> can resolve each executor's dependencies via
    /// <see cref="ActivatorUtilities"/> exactly as the host does.
    /// </summary>
    public static IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSingleton<IQuerySynthesisAgent>(sp => new QuerySynthesisAgentStub(sp.GetRequiredService<ILogger<QuerySynthesisAgentStub>>()));
        services.AddSingleton<IWebSearchAgent, FakeWebSearchAgent>();
        services.AddSingleton<IPreFilterStep>(sp => new PreFilterStep(sp.GetRequiredService<ILogger<PreFilterStep>>()));
        services.AddSingleton<IFetchAndCleanStep>(sp => new FetchAndCleanStep(
            new StubHttpClientFactory(),
            Options.Create(new FetchOptions()),
            sp.GetRequiredService<ILogger<FetchAndCleanStep>>()));
        services.AddSingleton<IRelevanceEvalAgent>(sp => new RelevanceEvalAgentStub(sp.GetRequiredService<ILogger<RelevanceEvalAgentStub>>()));
        services.AddSingleton<ILoopController>(sp => new LoopController(new StubFullTextStore(), sp.GetRequiredService<ILogger<LoopController>>()));
        services.AddSingleton<IVerdictRouting>(sp => new VerdictRouting(sp.GetRequiredService<ILogger<VerdictRouting>>()));
        services.AddSingleton<IEnrichmentAgent>(sp => new EnrichmentAgentStub(sp.GetRequiredService<ILogger<EnrichmentAgentStub>>()));
        services.AddSingleton<ICategorizeAgent>(sp => new CategorizeAgentStub(sp.GetRequiredService<ILogger<CategorizeAgentStub>>()));
        services.AddSingleton<ISummarizeImpactAgent>(sp => new SummarizeImpactAgentStub(sp.GetRequiredService<ILogger<SummarizeImpactAgentStub>>()));

        return services.BuildServiceProvider();
    }

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

    /// <summary>Like <see cref="Decision(Verdict[])"/> but with an explicit raw eval loop decision.</summary>
    public static ReviewDecision DecisionWith(LoopDecision decision, params Verdict[] verdicts)
    {
        var items = verdicts
            .Select((verdict, index) => new ItemVerdict { Index = index, Verdict = verdict, Rationale = "r" })
            .ToList();

        return new ReviewDecision
        {
            ThoughtProcess = "stub",
            Decision = decision,
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

/// <summary>
/// In-memory <see cref="IFullTextStore"/> for the pipeline tests: returns a deterministic fake blob
/// reference (matching the production key) without touching Azure Storage, and records what it persisted.
/// </summary>
internal sealed class StubFullTextStore : IFullTextStore
{
    public List<(string RunId, string GroupId, string ItemId, string Text)> Persisted { get; } = [];

    public Task<string> PersistAsync(string runId, string groupId, string itemId, string text, CancellationToken cancellationToken = default)
    {
        Persisted.Add((runId, groupId, itemId, text));
        return Task.FromResult($"https://stub.blob.core.windows.net/documents/fulltext/{runId}/{groupId}/{itemId}.txt");
    }
}

/// <summary>
/// Offline <see cref="IHttpClientFactory"/> for the pipeline tests: returns canned HTML for any URL so
/// the real <see cref="FetchAndCleanStep"/> exercises its fetch/clean path without touching the network.
/// </summary>
internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly string _html;

    public StubHttpClientFactory(string? html = null) =>
        _html = html ?? "<html><body><main>Canned fetched body text.</main></body></html>";

    public HttpClient CreateClient(string name) => new(new StubHandler(_html));

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _html;

        public StubHandler(string html) => _html = html;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_html, System.Text.Encoding.UTF8, "text/html"),
            };

            return Task.FromResult(response);
        }
    }
}
