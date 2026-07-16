using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Workflows.Agents;
using AgenticRagScannerApi.Workflows.Vocabulary;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Story 8.2 - the real Impact Area MAF agent over a fake <see cref="IChatClient"/> (no network). It runs
/// once per vetted document and picks a single impact area for that document. Covers Structured Outputs
/// deserialization, closed-set validation + canonical normalization, grounding on the vetted full text,
/// and safe degradation (off-list / failed / empty-vocabulary) that returns null rather than guessing.
/// </summary>
public class ImpactAreaAgentTests
{
    private static readonly IReadOnlyList<string> Vocabulary =
    [
        "Employer tax reporting/filing requirements",
        "Taxation of equity & incentives",
        "Employment taxes rates & thresholds",
    ];

    /// <summary>One vetted document, as the finalize step passes it in.</summary>
    private static ResultItem Item() => WorkflowTestFactory.Item("https://gov.uk/a", Verdict.Relevant);

    [Fact]
    public async Task SelectAsync_ReturnsImpactArea_WhenModelReturnsApprovedValue()
    {
        var chat = new FakeChatClient("""{"impactArea":"Employment taxes rates & thresholds","rationale":"changes NIC thresholds"}""");
        var agent = CreateAgent(chat);

        var result = await agent.SelectAsync(Item(), "full text about NIC thresholds", WorkflowTestFactory.CreateContext());

        result.Should().Be("Employment taxes rates & thresholds");
        chat.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SelectAsync_NormalizesToCanonicalSpelling_WhenModelDiffersInCasing()
    {
        var chat = new FakeChatClient("""{"impactArea":"  employment taxes rates & THRESHOLDS  "}""");
        var agent = CreateAgent(chat);

        var result = await agent.SelectAsync(Item(), "text", WorkflowTestFactory.CreateContext());

        result.Should().Be("Employment taxes rates & thresholds");
    }

    [Fact]
    public async Task SelectAsync_ReturnsNull_WhenModelReturnsOffListValue()
    {
        var chat = new FakeChatClient("""{"impactArea":"Something invented"}""");
        var agent = CreateAgent(chat);

        var result = await agent.SelectAsync(Item(), "text", WorkflowTestFactory.CreateContext());

        result.Should().BeNull();
    }

    [Fact]
    public async Task SelectAsync_ReturnsNull_AndSkipsModel_WhenVocabularyEmpty()
    {
        var chat = new FakeChatClient("""{"impactArea":"Employment taxes rates & thresholds"}""");
        var agent = CreateAgent(chat, vocabulary: []);

        var result = await agent.SelectAsync(Item(), "text", WorkflowTestFactory.CreateContext());

        result.Should().BeNull();
        chat.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SelectAsync_ReturnsNull_WhenModelCallFails()
    {
        var chat = new FakeChatClient("not json at all");
        var agent = CreateAgent(chat);

        var result = await agent.SelectAsync(Item(), "text", WorkflowTestFactory.CreateContext());

        result.Should().BeNull();
    }

    [Fact]
    public async Task SelectAsync_GroundsOnFullText_WhenProvided()
    {
        var chat = new FakeChatClient("""{"impactArea":"Taxation of equity & incentives"}""");
        var agent = CreateAgent(chat);

        await agent.SelectAsync(Item(), "SHARE-SCHEME-MARKER employee equity guidance", WorkflowTestFactory.CreateContext());

        chat.LastUserPrompt.Should().Contain("SHARE-SCHEME-MARKER");
    }

    [Fact]
    public async Task SelectAsync_NotesMissingFullText_WhenNull()
    {
        var chat = new FakeChatClient("""{"impactArea":"Employer tax reporting/filing requirements"}""");
        var agent = CreateAgent(chat);

        var result = await agent.SelectAsync(Item(), fullText: null, WorkflowTestFactory.CreateContext());

        chat.LastUserPrompt.Should().Contain("unavailable");
        result.Should().Be("Employer tax reporting/filing requirements");
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
