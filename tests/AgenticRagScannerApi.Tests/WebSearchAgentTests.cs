using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Core.Throttling;
using AgenticRagScannerApi.Workflows.Configuration;
using AgenticRagScannerApi.Workflows.Tools;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Locks in the Web Search agent's response mapping (story 4.1): URL citations from the hosted Foundry
/// agent become <see cref="Core.Contracts.SearchHit"/>s, with in-response de-duplication, a MaxResults
/// cap, an allowlist (defense-in-depth) drop, and graceful empty results. The agent is driven through a
/// real <see cref="ChatClientAgent"/> over a fake <see cref="IChatClient"/>, so the actual MAF response
/// pipeline runs without any Azure SDK.
/// </summary>
public class WebSearchAgentTests
{
    private static WebSearchOptions NewOptions(int maxResults = 10) => new()
    {
        ProjectEndpoint = "https://project.example.com",
        AgentName = "WebSearch",
        MaxResults = maxResults,
    };

    private static RunContext NewRun(IReadOnlyList<string>? allowlist = null) => new()
    {
        RunId = "run-1",
        Jurisdiction = "United Kingdom",
        AuthoritativeSources = allowlist ?? [],
    };

    private static WebSearchAgent NewSut(IChatClient chatClient, WebSearchOptions? options = null) =>
        new(
            new ChatClientAgent(chatClient),
            Options.Create(options ?? NewOptions()),
            new NoOpThrottle(),
            ResiliencePipeline.Empty,
            NullLogger<WebSearchAgent>.Instance);

    private static AIContent Citation(string url, string? title = null, string? snippet = null) =>
        new TextContent(string.Empty)
        {
            Annotations = [new CitationAnnotation { Url = new Uri(url, UriKind.RelativeOrAbsolute), Title = title, Snippet = snippet }],
        };

    private static IChatClient ChatClientReturning(params AIContent[] contents) =>
        new FakeChatClient(contents);

    [Fact]
    public async Task SearchAsync_MapsUrlCitationsToHits()
    {
        var chatClient = ChatClientReturning(
            Citation("https://www.gov.uk/a", "Title A", "Snippet A"),
            Citation("https://legislation.gov.uk/b", "Title B", "Snippet B"));

        var hits = await NewSut(chatClient).SearchAsync("query", NewRun());

        hits.Should().HaveCount(2);
        hits[0].Url.Should().Be("https://www.gov.uk/a");
        hits[0].Domain.Should().Be("www.gov.uk");
        hits[0].Title.Should().Be("Title A");
        hits[0].Snippet.Should().Be("Snippet A");
        hits[0].SourceQuery.Should().Be("query");
        hits[0].Rank.Should().Be(1);
        hits[1].Rank.Should().Be(2);
    }

    [Fact]
    public async Task SearchAsync_DeduplicatesIdenticalUrls()
    {
        var chatClient = ChatClientReturning(
            Citation("https://www.gov.uk/a"),
            Citation("https://www.gov.uk/a"));

        var hits = await NewSut(chatClient).SearchAsync("query", NewRun());

        hits.Should().ContainSingle();
    }

    [Fact]
    public async Task SearchAsync_CapsAtMaxResults()
    {
        var chatClient = ChatClientReturning(
            Citation("https://www.gov.uk/a"),
            Citation("https://www.gov.uk/b"),
            Citation("https://www.gov.uk/c"));

        var hits = await NewSut(chatClient, NewOptions(maxResults: 2)).SearchAsync("query", NewRun());

        hits.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchAsync_DropsOffAllowlistHosts()
    {
        var chatClient = ChatClientReturning(
            Citation("https://www.gov.uk/a"),
            Citation("https://evil.example.com/b"));

        var hits = await NewSut(chatClient).SearchAsync("query", NewRun(allowlist: ["www.gov.uk"]));

        hits.Should().ContainSingle();
        hits[0].Domain.Should().Be("www.gov.uk");
    }

    [Fact]
    public async Task SearchAsync_DropsNonHttpAndRelativeCitationUrls()
    {
        var chatClient = ChatClientReturning(
            Citation("ftp://www.gov.uk/a"),
            Citation("/relative/path"));

        var hits = await NewSut(chatClient).SearchAsync("query", NewRun());

        hits.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyWhenNoCitations()
    {
        var chatClient = ChatClientReturning(new TextContent("Just an answer, no sources."));

        var hits = await NewSut(chatClient).SearchAsync("query", NewRun());

        hits.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_RetriesTransientFailureThenSucceeds()
    {
        // Throws a transient error on the first attempt, then returns a valid citation on the second.
        var chatClient = new FlakyChatClient(
            failures: 1,
            success: new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                [Citation("https://www.gov.uk/a", "Title A", "Snippet A")])));

        var retryOnce = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = static args => ValueTask.FromResult(args.Outcome.Exception is HttpRequestException),
            })
            .Build();

        var sut = new WebSearchAgent(
            new ChatClientAgent(chatClient),
            Options.Create(NewOptions()),
            new NoOpThrottle(),
            retryOnce,
            NullLogger<WebSearchAgent>.Instance);

        var hits = await sut.SearchAsync("query", NewRun());

        chatClient.Attempts.Should().Be(2);
        hits.Should().ContainSingle();
        hits[0].Url.Should().Be("https://www.gov.uk/a");
    }

    /// <summary>A minimal in-memory <see cref="IChatClient"/> that returns a single canned assistant message.</summary>
    private sealed class FakeChatClient : IChatClient
    {
        private readonly ChatResponse _response;

        public FakeChatClient(IEnumerable<AIContent> contents) =>
            _response = new ChatResponse(new ChatMessage(ChatRole.Assistant, contents.ToList()));

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => Task.FromResult(_response);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>Throws a transient error for the first <c>failures</c> calls, then returns a canned response.</summary>
    private sealed class FlakyChatClient : IChatClient
    {
        private readonly int _failures;
        private readonly ChatResponse _success;

        public FlakyChatClient(int failures, ChatResponse success)
        {
            _failures = failures;
            _success = success;
        }

        public int Attempts { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (Attempts <= _failures)
            {
                throw new HttpRequestException("transient");
            }

            return Task.FromResult(_success);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
