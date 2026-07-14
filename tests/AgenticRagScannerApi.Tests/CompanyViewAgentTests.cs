using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Workflows.Agents;
using AgenticRagScannerApi.Workflows.Configuration;
using AgenticRagScannerApi.Workflows.CompanyView;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Story 8.5 - the real Company View MAF agent over a fake <see cref="IChatClient"/> (no network). It
/// produces ONE aggregate <see cref="CompanyViewRecord"/> per topic group: the dates and supporting
/// references are aggregated deterministically from the carried items, the impact area + tags are the
/// group-level categorization passed in by the finalize step, and the judgement fields (title, summary,
/// Company View, etc.) come from the model via Structured Outputs, steered by prior records retrieved by
/// jurisdiction. Covers grounding, exemplar capping, the empty-group short-circuit, and safe degradation.
/// </summary>
public class CompanyViewAgentTests
{
    private const string ModelJson =
        """{"titleOfUpdate":"NIC and fuel-rate changes","summaryOfUpdate":"Several employer-tax updates.","companyView":"Employers should update payroll systems.","levelOfAuthority":"Regulator guidance","statusOfChange":"In force","regulator":"HMRC"}""";

    private static readonly IReadOnlyDictionary<string, string?> NoFullText = new Dictionary<string, string?>();
    private static readonly IReadOnlyList<string> NoTags = [];

    private static ResultItem Item(string url) => WorkflowTestFactory.Item(url, Verdict.Relevant);

    private static CompanyViewAgent CreateAgent(IChatClient chat, IPriorCompanyViewSource source) =>
        new(chat, source, Options.Create(new CompanyViewOptions()), NullLogger<CompanyViewAgent>.Instance);

    [Fact]
    public async Task GenerateAsync_ReturnsNull_WhenNoItems()
    {
        var chat = new FakeChatClient(ModelJson);
        var agent = CreateAgent(chat, new FakeSource(Prior("p")));

        var record = await agent.GenerateAsync([], NoFullText, null, NoTags, WorkflowTestFactory.CreateContext());

        record.Should().BeNull();
        chat.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GenerateAsync_ProducesAggregateRecord_WithModelJudgementFields()
    {
        var chat = new FakeChatClient(ModelJson);
        var agent = CreateAgent(chat, new FakeSource(Prior("p")));
        var items = new[] { Item("https://gov.uk/a") };

        var record = await agent.GenerateAsync(items, NoFullText, "Employment taxes rates & thresholds", ["National Insurance"], WorkflowTestFactory.CreateContext());

        record.Should().NotBeNull();
        record!.CompanyView.Should().Be("Employers should update payroll systems.");
        record.TitleOfUpdate.Should().Be("NIC and fuel-rate changes");
        record.SummaryOfUpdate.Should().Be("Several employer-tax updates.");
        record.LevelOfAuthority.Should().Be("Regulator guidance");
        record.StatusOfChange.Should().Be("In force");
        record.Regulator.Should().Be("HMRC");
    }

    [Fact]
    public async Task GenerateAsync_StampsGroupLevelImpactAreaAndTags_AndAggregatesReferencesFromItems()
    {
        var chat = new FakeChatClient(ModelJson);
        var agent = CreateAgent(chat, new FakeSource([]));
        var items = new[]
        {
            Item("https://gov.uk/a"),
            Item("https://gov.uk/b"),
        };

        var record = await agent.GenerateAsync(
            items, NoFullText, "Employment taxes rates & thresholds", ["National Insurance", "IR35"], WorkflowTestFactory.CreateContext());

        record!.Jurisdiction.Should().Be("United Kingdom");
        record.ImpactArea.Should().Be("Employment taxes rates & thresholds");
        record.Tags.Should().BeEquivalentTo(new[] { "National Insurance", "IR35" });
        record.SupportingReference.Should().Contain("https://gov.uk/a").And.Contain("https://gov.uk/b");
    }

    [Fact]
    public async Task GenerateAsync_RetrievesPriorRecords_ByRunJurisdiction()
    {
        var chat = new FakeChatClient(ModelJson);
        var retriever = new FakeSource(Prior("p"));
        var agent = CreateAgent(chat, retriever);
        var context = WorkflowTestFactory.CreateContext();

        await agent.GenerateAsync(new[] { Item("https://gov.uk/a") }, NoFullText, null, NoTags, context);

        retriever.RequestedJurisdiction.Should().Be(context.Run.Jurisdiction);
    }

    [Fact]
    public async Task GenerateAsync_FeedsItemFullTextToModel()
    {
        var chat = new FakeChatClient(ModelJson);
        var agent = CreateAgent(chat, new FakeSource([]));
        var item = Item("https://gov.uk/a");
        var fullText = new Dictionary<string, string?> { [item.Id] = "FULL-TEXT-OF-THE-UPDATE about NIC thresholds" };

        await agent.GenerateAsync(new[] { item }, fullText, null, NoTags, WorkflowTestFactory.CreateContext());

        chat.LastUserPrompt.Should().Contain("FULL-TEXT-OF-THE-UPDATE about NIC thresholds");
    }

    [Fact]
    public async Task GenerateAsync_FeedsPriorRecordsToModel_AsExemplars()
    {
        var chat = new FakeChatClient(ModelJson);
        var agent = CreateAgent(chat, new FakeSource(Prior("HOUSE-STYLE-EXEMPLAR advice text")));

        await agent.GenerateAsync(new[] { Item("https://gov.uk/a") }, NoFullText, null, NoTags, WorkflowTestFactory.CreateContext());

        chat.LastUserPrompt.Should().Contain("HOUSE-STYLE-EXEMPLAR advice text");
    }

    [Fact]
    public async Task GenerateAsync_CapsExemplarsAtFive()
    {
        var many = Enumerable.Range(1, 9)
            .Select(i => new CompanyViewRecord { CompanyView = $"View number {i}" })
            .ToList();
        var chat = new FakeChatClient(ModelJson);
        var agent = CreateAgent(chat, new FakeSource(many));

        await agent.GenerateAsync(new[] { Item("https://gov.uk/a") }, NoFullText, null, NoTags, WorkflowTestFactory.CreateContext());

        chat.LastUserPrompt.Should().Contain("View number 5");
        chat.LastUserPrompt.Should().NotContain("View number 6");
    }

    [Fact]
    public async Task GenerateAsync_KeepsAggregatedFields_WhenModelCallFails()
    {
        var chat = new FakeChatClient("not json at all");
        var agent = CreateAgent(chat, new FakeSource([]));
        var items = new[] { Item("https://gov.uk/a") };

        var record = await agent.GenerateAsync(items, NoFullText, "Employment taxes rates & thresholds", ["National Insurance"], WorkflowTestFactory.CreateContext());

        record.Should().NotBeNull();
        record!.CompanyView.Should().BeNull();
        record.ImpactArea.Should().Be("Employment taxes rates & thresholds");
        record.Tags.Should().Contain("National Insurance");
        record.SupportingReference.Should().Contain("https://gov.uk/a");
    }

    private static List<CompanyViewRecord> Prior(string view) =>
        [new CompanyViewRecord { TitleOfUpdate = "Prior title", ImpactArea = "Impact area", SummaryOfUpdate = "Prior summary", CompanyView = view }];

    /// <summary>Deterministic <see cref="IPriorCompanyViewSource"/> returning a fixed list and recording the jurisdiction asked for.</summary>
    private sealed class FakeSource : IPriorCompanyViewSource
    {
        private readonly IReadOnlyList<CompanyViewRecord> _records;

        public FakeSource(IReadOnlyList<CompanyViewRecord> records) => _records = records;

        public string? RequestedJurisdiction { get; private set; }

        public Task<IReadOnlyList<CompanyViewRecord>> GetByJurisdictionAsync(string jurisdiction, CancellationToken cancellationToken = default)
        {
            RequestedJurisdiction = jurisdiction;
            return Task.FromResult(_records);
        }
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
