using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Workflows.Agents;
using AgenticRagScannerApi.Workflows.Vocabulary;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Story 8.2 - the real Impact Area MAF agent over a fake <see cref="IChatClient"/> (no network). It runs
/// ONCE per topic group and picks a single impact area across all the group's vetted updates. Covers
/// Structured Outputs deserialization, closed-set validation + canonical normalization, grounding on the
/// vetted full text, and safe degradation (off-list / failed / empty-vocabulary) that returns null rather
/// than guessing.
/// </summary>
public class ImpactAreaAgentTests
{
    private static readonly IReadOnlyList<string> Vocabulary =
    [
        "Employer tax reporting/filing requirements",
        "Taxation of equity & incentives",
        "Employment taxes rates & thresholds",
    ];

    /// <summary>One carried item plus its full-text map, as the finalize step passes them in.</summary>
    private static (IReadOnlyList<ResultItem> Items, IReadOnlyDictionary<string, string?> FullText) Group(string? fullText)
    {
        var item = WorkflowTestFactory.Item("https://gov.uk/a", Verdict.Relevant);
        return ([item], new Dictionary<string, string?> { [item.Id] = fullText });
    }

    [Fact]
    public async Task SelectAsync_ReturnsImpactArea_WhenModelReturnsApprovedValue()
    {
        var chat = new FakeChatClient("""{"impactArea":"Employment taxes rates & thresholds","rationale":"changes NIC thresholds"}""");
        var agent = CreateAgent(chat);
        var (items, fullText) = Group("full text about NIC thresholds");

        var result = await agent.SelectAsync(items, fullText, WorkflowTestFactory.CreateContext());

        result.Should().Be("Employment taxes rates & thresholds");
        chat.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SelectAsync_NormalizesToCanonicalSpelling_WhenModelDiffersInCasing()
    {
        var chat = new FakeChatClient("""{"impactArea":"  employment taxes rates & THRESHOLDS  "}""");
        var agent = CreateAgent(chat);
        var (items, fullText) = Group("text");

        var result = await agent.SelectAsync(items, fullText, WorkflowTestFactory.CreateContext());

        result.Should().Be("Employment taxes rates & thresholds");
    }

    [Fact]
    public async Task SelectAsync_ReturnsNull_WhenModelReturnsOffListValue()
    {
        var chat = new FakeChatClient("""{"impactArea":"Something invented"}""");
        var agent = CreateAgent(chat);
        var (items, fullText) = Group("text");

        var result = await agent.SelectAsync(items, fullText, WorkflowTestFactory.CreateContext());

        result.Should().BeNull();
    }

    [Fact]
    public async Task SelectAsync_ReturnsNull_AndSkipsModel_WhenVocabularyEmpty()
    {
        var chat = new FakeChatClient("""{"impactArea":"Employment taxes rates & thresholds"}""");
        var agent = CreateAgent(chat, vocabulary: []);
        var (items, fullText) = Group("text");

        var result = await agent.SelectAsync(items, fullText, WorkflowTestFactory.CreateContext());

        result.Should().BeNull();
        chat.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SelectAsync_ReturnsNull_AndSkipsModel_WhenNoItems()
    {
        var chat = new FakeChatClient("""{"impactArea":"Employment taxes rates & thresholds"}""");
        var agent = CreateAgent(chat);

        var result = await agent.SelectAsync([], new Dictionary<string, string?>(), WorkflowTestFactory.CreateContext());

        result.Should().BeNull();
        chat.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SelectAsync_ReturnsNull_WhenModelCallFails()
    {
        var chat = new FakeChatClient("not json at all");
        var agent = CreateAgent(chat);
        var (items, fullText) = Group("text");

        var result = await agent.SelectAsync(items, fullText, WorkflowTestFactory.CreateContext());

        result.Should().BeNull();
    }

    [Fact]
    public async Task SelectAsync_GroundsOnFullText_WhenProvided()
    {
        var chat = new FakeChatClient("""{"impactArea":"Taxation of equity & incentives"}""");
        var agent = CreateAgent(chat);
        var (items, fullText) = Group("SHARE-SCHEME-MARKER employee equity guidance");

        await agent.SelectAsync(items, fullText, WorkflowTestFactory.CreateContext());

        chat.LastUserPrompt.Should().Contain("SHARE-SCHEME-MARKER");
    }

    [Fact]
    public async Task SelectAsync_NotesMissingFullText_WhenNull()
    {
        var chat = new FakeChatClient("""{"impactArea":"Employer tax reporting/filing requirements"}""");
        var agent = CreateAgent(chat);
        var (items, fullText) = Group(fullText: null);

        var result = await agent.SelectAsync(items, fullText, WorkflowTestFactory.CreateContext());

        chat.LastUserPrompt.Should().Contain("unavailable");
        result.Should().Be("Employer tax reporting/filing requirements");
    }

    [Fact]
    public async Task SelectAsync_SingleItem_ReturnsImpactArea_AndGroundsOnFullText()
    {
        var chat = new FakeChatClient("""{"impactArea":"Employment taxes rates & thresholds"}""");
        var agent = CreateAgent(chat);
        var item = WorkflowTestFactory.Item("https://gov.uk/a", Verdict.Relevant);

        var result = await agent.SelectAsync(item, "SINGLE-ITEM full text about NIC thresholds", WorkflowTestFactory.CreateContext());

        result.Should().Be("Employment taxes rates & thresholds");
        chat.CallCount.Should().Be(1);
        chat.LastUserPrompt.Should().Contain("SINGLE-ITEM full text about NIC thresholds");
    }

    private static ImpactAreaAgent CreateAgent(IChatClient chatClient, IReadOnlyList<string>? vocabulary = null) =>
        new(chatClient, new FakeVocabularyProvider(vocabulary ?? Vocabulary), NullLogger<ImpactAreaAgent>.Instance);

    /// <summary>Deterministic <see cref="IRegulatoryVocabularyProvider"/> returning a fixed impact-area set.</summary>
    private sealed class FakeVocabularyProvider : IRegulatoryVocabularyProvider
    {
        private readonly IReadOnlyList<string> _impactAreas;

        public FakeVocabularyProvider(IReadOnlyList<string> impactAreas) => _impactAreas = impactAreas;

        public Task<IReadOnlyList<string>> GetImpactAreasAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_impactAreas);

        public Task<IReadOnlyList<string>> GetTagsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
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
