using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Workflows.Agents;
using AgenticRagScannerApi.Workflows.Vocabulary;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Story 8.3 - the real Tags MAF agent over a fake <see cref="IChatClient"/> (no network). It runs once
/// per vetted document and selects tags for that document. Covers Structured Outputs deserialization,
/// multi-label controlled-vocabulary validation + canonical normalization (drop off-list, dedupe),
/// grounding on the vetted full text, and safe degradation (empty vocabulary / failed call) that returns
/// an empty list.
/// </summary>
public class TagsAgentTests
{
    private static readonly IReadOnlyList<string> Vocabulary =
    [
        "Payroll Reporting",
        "National Insurance",
        "IR35",
        "Off Payroll",
    ];

    /// <summary>One vetted document, as the finalize step passes it in.</summary>
    private static ResultItem Item() => WorkflowTestFactory.Item("https://gov.uk/a", Verdict.Relevant);

    [Fact]
    public async Task SelectAsync_ReturnsTags_WhenModelReturnsApprovedValues()
    {
        var chat = new FakeChatClient("""{"tags":["IR35","Off Payroll"],"rationale":"contractor status changes"}""");
        var agent = CreateAgent(chat);

        var result = await agent.SelectAsync(Item(), "full text about off-payroll working", WorkflowTestFactory.CreateContext());

        result.Should().BeEquivalentTo(new[] { "IR35", "Off Payroll" });
        chat.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SelectAsync_NormalizesCasing_AndDropsOffListAndDuplicates()
    {
        var chat = new FakeChatClient("""{"tags":["  ir35  ","IR35","Something invented","national insurance"]}""");
        var agent = CreateAgent(chat);

        var result = await agent.SelectAsync(Item(), "text", WorkflowTestFactory.CreateContext());

        result.Should().Equal("IR35", "National Insurance");
    }

    [Fact]
    public async Task SelectAsync_ReturnsEmpty_WhenNoTagApplies()
    {
        var chat = new FakeChatClient("""{"tags":[]}""");
        var agent = CreateAgent(chat);

        var result = await agent.SelectAsync(Item(), "text", WorkflowTestFactory.CreateContext());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectAsync_ReturnsEmpty_AndSkipsModel_WhenVocabularyEmpty()
    {
        var chat = new FakeChatClient("""{"tags":["IR35"]}""");
        var agent = CreateAgent(chat, vocabulary: []);

        var result = await agent.SelectAsync(Item(), "text", WorkflowTestFactory.CreateContext());

        result.Should().BeEmpty();
        chat.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SelectAsync_ReturnsEmpty_WhenModelCallFails()
    {
        var chat = new FakeChatClient("not json at all");
        var agent = CreateAgent(chat);

        var result = await agent.SelectAsync(Item(), "text", WorkflowTestFactory.CreateContext());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectAsync_GroundsOnFullText_WhenProvided()
    {
        var chat = new FakeChatClient("""{"tags":["National Insurance"]}""");
        var agent = CreateAgent(chat);

        await agent.SelectAsync(Item(), "NIC-MARKER national insurance contributions guidance", WorkflowTestFactory.CreateContext());

        chat.LastUserPrompt.Should().Contain("NIC-MARKER");
    }

    private static TagsAgent CreateAgent(IChatClient chatClient, IReadOnlyList<string>? vocabulary = null) =>
        new(chatClient, new FakeVocabularyProvider(vocabulary ?? Vocabulary), NullLogger<TagsAgent>.Instance);

    /// <summary>Deterministic <see cref="IRegulatoryVocabularyProvider"/> returning a fixed tag set.</summary>
    private sealed class FakeVocabularyProvider : IRegulatoryVocabularyProvider
    {
        private readonly IReadOnlyList<string> _tags;

        public FakeVocabularyProvider(IReadOnlyList<string> tags) => _tags = tags;

        public Task<IReadOnlyList<string>> GetImpactAreasAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> GetTagsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_tags);
    }

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
