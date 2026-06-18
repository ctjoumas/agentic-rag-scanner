using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Steps;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Story 2.3 - verdict routing carries RELEVANT/BORDERLINE forward (borderline stays flagged via its
/// verdict) and drops NOT_RELEVANT.
/// </summary>
public class VerdictRoutingTests
{
    [Fact]
    public void Route_CarriesRelevantAndBorderline_DropsNotRelevant()
    {
        var context = WorkflowTestFactory.CreateContext();
        var pass = new LoopPass { Pass = 1, Query = "q" };
        var review = new Review
        {
            ThoughtProcess = "t",
            LlmDecision = LoopDecision.Finalize,
            FinalDecision = LoopDecision.Finalize,
        };
        review.Vetted.Add(WorkflowTestFactory.Item("https://gov.uk/a", Verdict.Relevant));
        review.Vetted.Add(WorkflowTestFactory.Item("https://gov.uk/b", Verdict.Borderline));
        review.Discarded.Add(WorkflowTestFactory.Item("https://gov.uk/c", Verdict.NotRelevant));
        pass.Review = review;
        context.History.Passes.Add(pass);

        var routing = new VerdictRouting(NullLogger<VerdictRouting>.Instance);
        var carried = routing.Route(context);

        carried.Should().HaveCount(2);
        carried.Select(i => i.Verdict).Should().BeEquivalentTo(new[] { Verdict.Relevant, Verdict.Borderline });
    }
}
