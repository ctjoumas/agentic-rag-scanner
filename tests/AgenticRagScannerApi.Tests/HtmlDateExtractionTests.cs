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
/// Story: HTML date-metadata preservation. GOV.UK (and similar) render the Published/Updated dates in the
/// page <c>&lt;head&gt;</c> metas and a header metadata block that live OUTSIDE <c>&lt;main&gt;</c>, so the
/// cleaner's boilerplate-strip + <c>&lt;main&gt;</c>-only extraction dropped them and the eval agent saw no
/// dates. The extractor now harvests any value that parses as a date (via the framework parser, no regex)
/// from meta / time / dl carriers - keeping the page's own label - and prepends them to the cleaned text.
/// Exercised through the public <see cref="FetchAndCleanStep"/> (the internal extractor is not visible).
/// </summary>
public sealed class HtmlDateExtractionTests
{
    // Trimmed but structurally faithful to a GOV.UK HMRC manual page: date metas + a metadata <dl> that
    // both sit outside <main>, a super-nav to prove chrome is still stripped, and the article body.
    private const string GovUkLikeHtml = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta name="csrf-token" content="6rgKcLo4RZpcL7sYXPPRABfHU19QiGxpiXWjNWgf0iLg">
          <meta name="theme-color" content="#1d70b8">
          <meta name="govuk:content-id" content="d9080621-ed20-5412-ae53-e95349fcc070">
          <meta name="govuk:first-published-at" content="2014-05-22T10:00:00+01:00">
          <meta name="govuk:public-updated-at" content="2022-04-06T11:13:47+01:00">
          <title>EIM42769 - Salary sacrifice</title>
        </head>
        <body>
          <header class="super-nav">
            <nav aria-label="menu">
              <a href="/browse/benefits">Benefits</a>
              <a href="/browse/tax">Money and tax</a>
            </nav>
          </header>
          <header aria-labelledby="manual-title" class="gem-c-inverse-header">
            <div class="gem-c-metadata">
              <dl class="gem-c-metadata__list">
                <dt>From:</dt><dd><a href="/x">HM Revenue &amp; Customs</a></dd>
                <dt>Published:</dt><dd>22 May 2014</dd>
                <dt>Updated:</dt>
                <dd>15 July 2026 - <a href="/updates">See all updates</a></dd>
              </dl>
            </div>
          </header>
          <main id="content">
            <article>
              <h1>EIM42769 - Salary sacrifice: effectiveness of contractual arrangement</h1>
              <p>Section 62 ITEPA 2003. From 6 April 2017, the Income Tax and NICs advantages are largely withdrawn.</p>
            </article>
          </main>
          <footer><a href="/help">Help</a></footer>
        </body>
        </html>
        """;

    private static FetchAndCleanStep StepReturning(string html) =>
        new(new HtmlHttpClientFactory(html), Options.Create(new FetchOptions()), NullLogger<FetchAndCleanStep>.Instance);

    private static SearchHit Hit(string url = "https://www.gov.uk/x") => new() { Url = url, SourceQuery = "q" };

    [Fact]
    public async Task FetchAsync_SurfacesPublicationAndUpdatedDates_FromHeadMetasAndMetadataBlock()
    {
        var result = await StepReturning(GovUkLikeHtml).FetchAsync(Hit());

        result.Unverified.Should().BeFalse();
        var text = result.CleanedText!;

        // Head metas (label passed through verbatim, ISO-normalized).
        text.Should().Contain("first-published-at: 2014-05-22");
        text.Should().Contain("public-updated-at: 2022-04-06");

        // Visible metadata block: human labels + long-form dates, with trailing text tolerated.
        text.Should().Contain("Published: 2014-05-22");
        text.Should().Contain("Updated: 2026-07-15");

        // The article body is still preserved...
        text.Should().Contain("Section 62 ITEPA 2003");
        // ...and the chrome + junk metadata are still stripped.
        text.Should().NotContain("Money and tax");
        text.Should().NotContain("csrf");
        text.Should().NotContain("d9080621");
    }

    [Fact]
    public async Task FetchAsync_OmitsDateBlock_WhenPageHasNoParseableDates()
    {
        const string html = "<html><body><main><p>Guidance about VAT registration with no dates here.</p></main></body></html>";

        var result = await StepReturning(html).FetchAsync(Hit());

        result.CleanedText.Should().NotContain("Dates found on page");
        result.CleanedText.Should().Contain("Guidance about VAT registration");
    }

    /// <summary>Returns an <see cref="HttpClient"/> that serves fixed <c>text/html</c> for any request.</summary>
    private sealed class HtmlHttpClientFactory(string html) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(html));

        private sealed class Handler(string html) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(html, Encoding.UTF8, "text/html"),
                });
        }
    }
}
