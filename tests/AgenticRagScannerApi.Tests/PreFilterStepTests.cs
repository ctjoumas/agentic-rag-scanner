using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Steps;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Locks in the deterministic pre-filter (story 4.3): URL validity, canonicalization, in-group dedupe
/// (incl. earlier passes), and run-level cross-group dedupe. These are pure functions over the two
/// seen-sets, so no I/O or mocks are needed.
/// </summary>
public class PreFilterStepTests
{
    private static PreFilterStep NewStep() => new(NullLogger<PreFilterStep>.Instance);

    private static RunContext NewRun() => new()
    {
        RunId = "run-1",
        Jurisdiction = "United Kingdom",
        AuthoritativeSources = [],
    };

    private static TopicGroupContext NewContext(RunContext run, string groupId) => new()
    {
        Run = run,
        TopicGroup = new TopicGroup { Id = groupId, Name = groupId, Keywords = [groupId], MaxLoops = 3 },
    };

    private static SearchHit Hit(string url) => new() { Url = url, SourceQuery = "q" };

    [Fact]
    public void Filter_KeepsValidHttpAndHttpsHits()
    {
        var ctx = NewContext(NewRun(), "tax");

        var kept = NewStep().Filter(
            [Hit("https://www.gov.uk/a"), Hit("http://legislation.gov.uk/b")], ctx);

        kept.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("mailto:someone@gov.uk")]
    [InlineData("ftp://gov.uk/file")]
    [InlineData("/relative/path")]
    [InlineData("not a url")]
    [InlineData("")]
    public void Filter_DropsInvalidOrNonHttpUrls(string url)
    {
        var ctx = NewContext(NewRun(), "tax");

        var kept = NewStep().Filter([Hit(url)], ctx);

        kept.Should().BeEmpty();
    }

    [Fact]
    public void Filter_DropsDuplicateWithinTheSameBatch()
    {
        var ctx = NewContext(NewRun(), "tax");

        var kept = NewStep().Filter(
            [Hit("https://gov.uk/page"), Hit("https://gov.uk/page")], ctx);

        kept.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("https://www.gov.uk/page", "https://gov.uk/page")]                 // www. prefix
    [InlineData("https://gov.uk/page/", "https://gov.uk/page")]                    // trailing slash
    [InlineData("https://GOV.UK/Page", "https://gov.uk/Page")]                     // host casing
    [InlineData("https://gov.uk/page#section", "https://gov.uk/page")]             // fragment
    [InlineData("https://gov.uk/page?utm_source=x", "https://gov.uk/page")]        // tracking param
    [InlineData("https://gov.uk/page?a=1&b=2", "https://gov.uk/page?b=2&a=1")]     // query order
    public void Filter_CollapsesEquivalentUrlForms(string first, string second)
    {
        var ctx = NewContext(NewRun(), "tax");

        var kept = NewStep().Filter([Hit(first), Hit(second)], ctx);

        kept.Should().HaveCount(1, "the two forms canonicalize to the same dedupe key");
    }

    [Fact]
    public void Filter_KeepsUrlsThatDifferOnlyByNonTrackingQuery()
    {
        var ctx = NewContext(NewRun(), "tax");

        var kept = NewStep().Filter(
            [Hit("https://gov.uk/search?id=1"), Hit("https://gov.uk/search?id=2")], ctx);

        kept.Should().HaveCount(2, "distinct query parameters can select distinct content");
    }

    [Fact]
    public void Filter_DropsUrlAlreadySeenInAnEarlierPassOfTheSameGroup()
    {
        var step = NewStep();
        var ctx = NewContext(NewRun(), "tax");

        step.Filter([Hit("https://gov.uk/page")], ctx);          // pass 1
        var pass2 = step.Filter([Hit("https://gov.uk/page")], ctx); // pass 2

        pass2.Should().BeEmpty();
    }

    [Fact]
    public void Filter_DropsUrlAlreadySurfacedByAnotherGroupInTheSameRun()
    {
        var step = NewStep();
        var run = NewRun();
        var groupA = NewContext(run, "tax");
        var groupB = NewContext(run, "payroll");

        var keptA = step.Filter([Hit("https://gov.uk/shared")], groupA);
        var keptB = step.Filter([Hit("https://gov.uk/shared")], groupB);

        keptA.Should().HaveCount(1);
        keptB.Should().BeEmpty("the URL was already surfaced by another group this run (cross-group dedupe)");
    }

    [Fact]
    public void Filter_KeepsSameUrlForDifferentRuns()
    {
        var step = NewStep();
        var url = "https://gov.uk/shared";

        var run1 = step.Filter([Hit(url)], NewContext(NewRun(), "tax"));
        var run2 = step.Filter([Hit(url)], NewContext(NewRun(), "tax"));

        run1.Should().HaveCount(1);
        run2.Should().HaveCount(1, "cross-group dedupe is scoped to a single run");
    }
}
