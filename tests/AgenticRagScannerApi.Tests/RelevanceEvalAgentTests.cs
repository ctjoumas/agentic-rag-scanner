using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Agents;
using AgenticRagScannerApi.Workflows.Pipeline;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Story 6.1 - the real Relevance Eval MAF agent over a fake <see cref="IChatClient"/> (no network).
/// Covers Structured Outputs deserialization into verdicts + effective-date fields, conservative handling
/// of indices the model omits, and the BORDERLINE/RETRY fallback when the model output is unusable (a
/// failed eval must never silently drop a candidate - compliance prizes recall).
/// </summary>
public class RelevanceEvalAgentTests
{
    [Fact]
    public async Task EvaluateAsync_ParsesVerdictsAndDates()
    {
        const string json = """
        {
          "thoughtProcess": "Missing facets: none. Weak evidence: none.",
          "decision": "FINALIZE",
          "items": [
            { "index": 0, "verdict": "RELEVANT", "rationale": "primary source",
              "publicationDate": "2025-09-01", "effectiveDate": "2025-09-01",
              "appliesFrom": "2025-09-01", "appliesTo": "2025-12-31", "dateConfidence": "HIGH" },
            { "index": 1, "verdict": "NOT_RELEVANT", "rationale": "off topic", "dateConfidence": "UNKNOWN" }
          ]
        }
        """;
        var agent = CreateAgent(new FakeChatClient(json));
        var context = WorkflowTestFactory.CreateContext(name: "Advisory Fuel Rates");
        context.History.Passes.Add(new LoopPass { Pass = 1, Query = "q" });
        var documents = Docs("https://gov.uk/a", "https://blog.example/b");

        var decision = await agent.EvaluateAsync(context, documents);

        decision.Decision.Should().Be(LoopDecision.Finalize);
        decision.Items.Should().HaveCount(2);

        var first = decision.Items[0];
        first.Index.Should().Be(0);
        first.Verdict.Should().Be(Verdict.Relevant);
        first.PublicationDate.Should().Be(new DateOnly(2025, 9, 1));
        first.EffectiveDate.Should().Be(new DateOnly(2025, 9, 1));
        first.AppliesFrom.Should().Be(new DateOnly(2025, 9, 1));
        first.AppliesTo.Should().Be(new DateOnly(2025, 12, 31));
        first.DateConfidence.Should().Be(DateConfidence.High);

        decision.Items[1].Verdict.Should().Be(Verdict.NotRelevant);
        decision.Items[1].DateConfidence.Should().Be(DateConfidence.Unknown);
    }

    [Fact]
    public async Task EvaluateAsync_FillsOmittedIndices_AsBorderline()
    {
        // The model only returns a verdict for index 0; index 1 must be carried as BORDERLINE, never dropped.
        const string json = """
        { "thoughtProcess": "...", "decision": "RETRY",
          "items": [ { "index": 0, "verdict": "RELEVANT" } ] }
        """;
        var agent = CreateAgent(new FakeChatClient(json));
        var context = WorkflowTestFactory.CreateContext();
        context.History.Passes.Add(new LoopPass { Pass = 1, Query = "q" });
        var documents = Docs("https://gov.uk/a", "https://gov.uk/b");

        var decision = await agent.EvaluateAsync(context, documents);

        decision.Items.Should().HaveCount(2);
        decision.Items.Should().ContainSingle(v => v.Index == 1 && v.Verdict == Verdict.Borderline);
    }

    [Fact]
    public async Task EvaluateAsync_FallsBackToBorderlineRetry_WhenModelOutputUnusable()
    {
        var agent = CreateAgent(new FakeChatClient("not json"));
        var context = WorkflowTestFactory.CreateContext();
        context.History.Passes.Add(new LoopPass { Pass = 1, Query = "q" });
        var documents = Docs("https://gov.uk/a", "https://gov.uk/b");

        var decision = await agent.EvaluateAsync(context, documents);

        decision.Decision.Should().Be(LoopDecision.Retry);
        decision.Items.Should().HaveCount(2);
        decision.Items.Should().OnlyContain(v => v.Verdict == Verdict.Borderline);
    }

    [Fact]
    public async Task EvaluateAsync_NoDocuments_FinalizesWithoutCallingModel()
    {
        var chat = new FakeChatClient("{}");
        var agent = CreateAgent(chat);
        var context = WorkflowTestFactory.CreateContext();
        context.History.Passes.Add(new LoopPass { Pass = 1, Query = "q" });

        var decision = await agent.EvaluateAsync(context, []);

        decision.Decision.Should().Be(LoopDecision.Finalize);
        decision.Items.Should().BeEmpty();
        chat.CallCount.Should().Be(0);
    }

    private static RelevanceEvalAgent CreateAgent(IChatClient chatClient) =>
        new(chatClient, NullLogger<RelevanceEvalAgent>.Instance);

    private static List<FetchedDocument> Docs(params string[] urls) =>
        urls.Select(WorkflowTestFactory.Doc).ToList();

    /// <summary>Deterministic <see cref="IChatClient"/> that returns canned responses in order.</summary>
    private sealed class FakeChatClient : IChatClient
    {
        private readonly Queue<string> _responses;

        public FakeChatClient(params string[] responses) => _responses = new Queue<string>(responses);

        public int CallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var text = _responses.Count > 0 ? _responses.Dequeue() : "{}";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
