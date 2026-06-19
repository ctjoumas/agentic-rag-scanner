using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Agents;
using AgenticRagScannerApi.Workflows.Configuration;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Story 3.3 - the real Query Synthesis MAF agent over a fake <see cref="IChatClient"/> (no network).
/// Covers JSON parsing, bounded retry on invalid JSON, deterministic fallback, SearchHistory-aware
/// redundancy avoidance, and de-dupe/cap of the model's queries.
/// </summary>
public class QuerySynthesisAgentTests
{
    [Fact]
    public async Task SynthesizeAsync_ParsesQueriesFromModelJson()
    {
        var chat = new FakeChatClient("""{"queries":["uk advisory fuel rates update","HMRC mileage rates change"]}""");
        var agent = CreateAgent(chat);
        var context = WorkflowTestFactory.CreateContext();

        var queries = await agent.SynthesizeAsync(context);

        queries.Should().Equal("uk advisory fuel rates update", "HMRC mileage rates change");
        chat.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SynthesizeAsync_RetriesOnInvalidJson_ThenSucceeds()
    {
        var chat = new FakeChatClient("not json at all", """{"queries":["recovered query"]}""");
        var agent = CreateAgent(chat, new QuerySynthesisOptions { MaxAttempts = 2 });
        var context = WorkflowTestFactory.CreateContext();

        var queries = await agent.SynthesizeAsync(context);

        queries.Should().ContainSingle().Which.Should().Be("recovered query");
        chat.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task SynthesizeAsync_FallsBackToDeterministicQuery_WhenAllAttemptsInvalid()
    {
        var chat = new FakeChatClient("nope", "still nope");
        var agent = CreateAgent(chat, new QuerySynthesisOptions { MaxAttempts = 2 });
        var context = WorkflowTestFactory.CreateContext(name: "Advisory Fuel Rates");

        var queries = await agent.SynthesizeAsync(context);

        queries.Should().ContainSingle();
        queries[0].Should().Contain("Advisory Fuel Rates").And.Contain("update");
        chat.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task SynthesizeAsync_FeedsPriorQueriesToModel_ToAvoidRedundancy()
    {
        var chat = new FakeChatClient("""{"queries":["fresh angle"]}""");
        var agent = CreateAgent(chat);
        var context = WorkflowTestFactory.CreateContext();
        context.History.Passes.Add(new LoopPass { Pass = 1, Query = "previously tried query" });

        await agent.SynthesizeAsync(context);

        chat.LastUserPrompt.Should().Contain("previously tried query");
    }

    [Fact]
    public async Task SynthesizeAsync_DeduplicatesAndCapsQueries()
    {
        var chat = new FakeChatClient("""{"queries":["A","A","B","C","D","E"]}""");
        var agent = CreateAgent(chat, new QuerySynthesisOptions { MaxQueries = 4 });
        var context = WorkflowTestFactory.CreateContext();

        var queries = await agent.SynthesizeAsync(context);

        queries.Should().Equal("A", "B", "C", "D");
    }

    private static QuerySynthesisAgent CreateAgent(IChatClient chatClient, QuerySynthesisOptions? options = null) =>
        new(chatClient, Options.Create(options ?? new QuerySynthesisOptions()), NullLogger<QuerySynthesisAgent>.Instance);

    /// <summary>Deterministic <see cref="IChatClient"/> that returns canned responses in order and records the user prompt.</summary>
    private sealed class FakeChatClient : IChatClient
    {
        private readonly Queue<string> _responses;

        public FakeChatClient(params string[] responses) => _responses = new Queue<string>(responses);

        public int CallCount { get; private set; }

        public string LastUserPrompt { get; private set; } = string.Empty;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastUserPrompt = string.Join("\n", messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));

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
