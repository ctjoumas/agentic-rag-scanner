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
/// Covers Structured Outputs deserialization, bounded retry when the model output is unusable,
/// deterministic fallback, and SearchHistory-aware redundancy avoidance. The agent emits a single query
/// per pass - breadth comes from the agentic loop.
/// </summary>
public class QuerySynthesisAgentTests
{
    [Fact]
    public async Task SynthesizeAsync_ReturnsStructuredQuery()
    {
        var chat = new FakeChatClient("""{"query":"uk advisory fuel rates update","rationale":"broad first-pass query covering the whole theme"}""");
        var agent = CreateAgent(chat);
        var context = WorkflowTestFactory.CreateContext();

        var result = await agent.SynthesizeAsync(context);

        result.Query.Should().Be("uk advisory fuel rates update");
        result.Rationale.Should().Be("broad first-pass query covering the whole theme");
        chat.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SynthesizeAsync_RetriesWhenModelOutputUnusable_ThenSucceeds()
    {
        var chat = new FakeChatClient("not json at all", """{"query":"recovered query"}""");
        var agent = CreateAgent(chat, new QuerySynthesisOptions { MaxAttempts = 2 });
        var context = WorkflowTestFactory.CreateContext();

        var result = await agent.SynthesizeAsync(context);

        result.Query.Should().Be("recovered query");
        chat.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task SynthesizeAsync_FallsBackToDeterministicQuery_WhenAllAttemptsInvalid()
    {
        var chat = new FakeChatClient("nope", "still nope");
        var agent = CreateAgent(chat, new QuerySynthesisOptions { MaxAttempts = 2 });
        var context = WorkflowTestFactory.CreateContext(name: "Advisory Fuel Rates");

        var result = await agent.SynthesizeAsync(context);

        result.Query.Should().Contain("Advisory Fuel Rates").And.Contain("update");
        result.Rationale.Should().NotBeNullOrWhiteSpace();
        chat.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task SynthesizeAsync_FeedsPriorQueriesToModel_ToAvoidRedundancy()
    {
        var chat = new FakeChatClient("""{"query":"fresh angle"}""");
        var agent = CreateAgent(chat);
        var context = WorkflowTestFactory.CreateContext();
        context.History.Passes.Add(new LoopPass { Pass = 1, Query = "previously tried query" });

        await agent.SynthesizeAsync(context);

        chat.LastUserPrompt.Should().Contain("previously tried query");
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
