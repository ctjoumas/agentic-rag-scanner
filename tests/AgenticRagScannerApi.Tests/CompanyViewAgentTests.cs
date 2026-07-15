using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Workflows.Agents;
using AgenticRagScannerApi.Workflows.Configuration;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Story 8.5 - the real Company View MAF agent over a fake <see cref="IChatClient"/> (no network). It
/// produces ONE <see cref="CompanyViewRecord"/> per vetted document : the dates and supporting reference
/// are set deterministically from the item, the impact area + tags are the per-document categorization
/// passed in by the finalize step, and the judgement fields (title, summary, Company View, etc.) come
/// from the model via Structured Outputs, steered by prior exemplars passed in. Covers grounding, exemplar
/// pass-through, and safe degradation.
/// </summary>
public class CompanyViewAgentTests
{
    private const string ModelJson =
        """{"titleOfUpdate":"NIC and fuel-rate changes","summaryOfUpdate":"Several employer-tax updates.","companyView":"Employers should update payroll systems.","levelOfAuthority":"Regulator guidance","statusOfChange":"In force","regulator":"HMRC"}""";

    private static readonly IReadOnlyList<string> NoTags = [];
    private static readonly IReadOnlyList<CompanyViewRecord> NoPriors = [];

    private static ResultItem Item(string url) => WorkflowTestFactory.Item(url, Verdict.Relevant);

    private static CompanyViewAgent CreateAgent(IChatClient chat) =>
        new(chat, Options.Create(new CompanyViewOptions()), NullLogger<CompanyViewAgent>.Instance);

    [Fact]
    public async Task GenerateAsync_ProducesRecord_WithModelJudgementFields()
    {
        var chat = new FakeChatClient(ModelJson);
        var agent = CreateAgent(chat);

        var record = await agent.GenerateAsync(
            Item("https://gov.uk/a"), "full text", "Employment taxes rates & thresholds", ["National Insurance"], NoPriors, WorkflowTestFactory.CreateContext());

        record.Should().NotBeNull();
        record!.CompanyView.Should().Be("Employers should update payroll systems.");
        record.TitleOfUpdate.Should().Be("NIC and fuel-rate changes");
        record.SummaryOfUpdate.Should().Be("Several employer-tax updates.");
        record.LevelOfAuthority.Should().Be("Regulator guidance");
        record.StatusOfChange.Should().Be("In force");
        record.Regulator.Should().Be("HMRC");
    }

    [Fact]
    public async Task GenerateAsync_StampsImpactAreaAndTags_AndSetsSupportingReferenceFromItem()
    {
        var chat = new FakeChatClient(ModelJson);
        var agent = CreateAgent(chat);

        var record = await agent.GenerateAsync(
            Item("https://gov.uk/a"), null, "Employment taxes rates & thresholds", ["National Insurance", "IR35"], NoPriors, WorkflowTestFactory.CreateContext());

        record!.Jurisdiction.Should().Be("United Kingdom");
        record.ImpactArea.Should().Be("Employment taxes rates & thresholds");
        record.Tags.Should().BeEquivalentTo(new[] { "National Insurance", "IR35" });
        record.SupportingReference.Should().Contain("https://gov.uk/a");
    }

    [Fact]
    public async Task GenerateAsync_FeedsItemFullTextToModel()
    {
        var chat = new FakeChatClient(ModelJson);
        var agent = CreateAgent(chat);

        await agent.GenerateAsync(
            Item("https://gov.uk/a"), "FULL-TEXT-OF-THE-UPDATE about NIC thresholds", null, NoTags, NoPriors, WorkflowTestFactory.CreateContext());

        chat.LastUserPrompt.Should().Contain("FULL-TEXT-OF-THE-UPDATE about NIC thresholds");
    }

    [Fact]
    public async Task GenerateAsync_FeedsPassedInExemplarsToModel()
    {
        var chat = new FakeChatClient(ModelJson);
        var agent = CreateAgent(chat);

        await agent.GenerateAsync(
            Item("https://gov.uk/a"), null, null, NoTags, Prior("HOUSE-STYLE-EXEMPLAR advice text"), WorkflowTestFactory.CreateContext());

        chat.LastUserPrompt.Should().Contain("HOUSE-STYLE-EXEMPLAR advice text");
    }

    [Fact]
    public async Task GenerateAsync_KeepsObjectiveFields_WhenModelCallFails()
    {
        var chat = new FakeChatClient("not json at all");
        var agent = CreateAgent(chat);

        var record = await agent.GenerateAsync(
            Item("https://gov.uk/a"), null, "Employment taxes rates & thresholds", ["National Insurance"], NoPriors, WorkflowTestFactory.CreateContext());

        record.Should().NotBeNull();
        record!.CompanyView.Should().BeNull();
        record.ImpactArea.Should().Be("Employment taxes rates & thresholds");
        record.Tags.Should().Contain("National Insurance");
        record.SupportingReference.Should().Contain("https://gov.uk/a");
    }

    private static List<CompanyViewRecord> Prior(string view) =>
        [new CompanyViewRecord { TitleOfUpdate = "Prior title", ImpactArea = "Impact area", SummaryOfUpdate = "Prior summary", CompanyView = view }];

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
