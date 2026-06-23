using System.Net;
using System.Text;
using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Workflows.Configuration;
using AgenticRagScannerApi.Workflows.Steps;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Locks in the real Fetch &amp; clean step (Epic 5, story 5.2): HTML/PDF cleaning, the snippet fallback
/// (never drop, flag <see cref="FetchedDocument.Unverified"/>), and the size/content-type/scheme caps.
/// All HTTP is served by an in-memory handler - no network.
/// </summary>
public class FetchAndCleanStepTests
{
    private static SearchHit Hit(string url, string? snippet = "Bing snippet text.") =>
        new() { Url = url, SourceQuery = "q", Snippet = snippet };

    private static FetchAndCleanStep NewStep(HttpResponseFactory factory, FetchOptions? options = null) =>
        new(
            new FakeHttpClientFactory(new FakeHandler(factory)),
            Options.Create(options ?? new FetchOptions()),
            NullLogger<FetchAndCleanStep>.Instance);

    [Fact]
    public async Task Fetch_CleansHtmlBoilerplate_AndReturnsVerifiedText()
    {
        const string html = """
            <html><head><style>.x{color:red}</style></head>
            <body><nav>Home | About</nav><script>track()</script>
            <main><h1>VAT update</h1><p>The rate changes in April.</p></main>
            <footer>Crown copyright</footer></body></html>
            """;
        var step = NewStep(_ => Html(html));

        var doc = await step.FetchAsync(Hit("https://www.gov.uk/vat"));

        doc.Unverified.Should().BeFalse();
        doc.CleanedText.Should().Contain("VAT update").And.Contain("The rate changes in April.");
        doc.CleanedText.Should().NotContain("track()").And.NotContain("Home | About").And.NotContain("Crown copyright");
    }

    [Theory]
    [InlineData("ftp://gov.uk/file")]
    [InlineData("mailto:a@gov.uk")]
    [InlineData("not a url")]
    public async Task Fetch_NonHttpUrl_FallsBackToSnippet(string url)
    {
        var step = NewStep(_ => throw new InvalidOperationException("must not be called"));

        var doc = await step.FetchAsync(Hit(url));

        doc.Unverified.Should().BeTrue();
        doc.CleanedText.Should().Be("Bing snippet text.");
    }

    [Fact]
    public async Task Fetch_NonSuccessStatus_FallsBackToSnippet()
    {
        var step = NewStep(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var doc = await step.FetchAsync(Hit("https://www.gov.uk/missing"));

        doc.Unverified.Should().BeTrue();
        doc.CleanedText.Should().Be("Bing snippet text.");
    }

    [Fact]
    public async Task Fetch_DisallowedContentType_FallsBackToSnippet()
    {
        var step = NewStep(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"a\":1}", Encoding.UTF8, "application/json"),
            };
            return response;
        });

        var doc = await step.FetchAsync(Hit("https://www.gov.uk/data.json"));

        doc.Unverified.Should().BeTrue();
        doc.CleanedText.Should().Be("Bing snippet text.");
    }

    [Fact]
    public async Task Fetch_BodyOverSizeCap_FallsBackToSnippet()
    {
        var big = new string('a', 2 * 1024 * 1024); // 2 MB of body
        var html = $"<html><body><main>{big}</main></body></html>";
        var step = NewStep(_ => Html(html), new FetchOptions { MaxResponseMegabytes = 1 });

        var doc = await step.FetchAsync(Hit("https://www.gov.uk/huge"));

        doc.Unverified.Should().BeTrue();
        doc.CleanedText.Should().Be("Bing snippet text.");
    }

    [Fact]
    public async Task Fetch_TransportError_FallsBackToSnippet()
    {
        var step = NewStep(_ => throw new HttpRequestException("connection reset"));

        var doc = await step.FetchAsync(Hit("https://www.gov.uk/flaky"));

        doc.Unverified.Should().BeTrue();
        doc.CleanedText.Should().Be("Bing snippet text.");
    }

    [Fact]
    public async Task Fetch_EmptyCleanedText_FallsBackToSnippet()
    {
        var step = NewStep(_ => Html("<html><body><nav>only boilerplate</nav></body></html>"));

        var doc = await step.FetchAsync(Hit("https://www.gov.uk/empty"));

        doc.Unverified.Should().BeTrue();
        doc.CleanedText.Should().Be("Bing snippet text.");
    }

    [Fact]
    public async Task Fetch_FailureWithNoSnippet_ReturnsNullCleanedTextButNeverDrops()
    {
        var step = NewStep(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var doc = await step.FetchAsync(Hit("https://www.gov.uk/x", snippet: null));

        doc.Should().NotBeNull();
        doc.Unverified.Should().BeTrue();
        doc.CleanedText.Should().BeNull();
        doc.Hit.Url.Should().Be("https://www.gov.uk/x");
    }

    [Fact]
    public async Task Fetch_CallerCancellation_Propagates()
    {
        var step = NewStep(_ => throw new OperationCanceledException());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await step.FetchAsync(Hit("https://www.gov.uk/slow"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(html, Encoding.UTF8, "text/html"),
    };

    private delegate HttpResponseMessage HttpResponseFactory(HttpRequestMessage request);

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpResponseFactory _factory;

        public FakeHandler(HttpResponseFactory factory) => _factory = factory;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_factory(request));
        }
    }
}
