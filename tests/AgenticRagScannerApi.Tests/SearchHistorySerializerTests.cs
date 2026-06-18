using System.Text.Json;
using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Pipeline;
using FluentAssertions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Story 2.1 - the SearchHistory checkpoint snapshot survives a JSON serialize/deserialize round-trip
/// (this is the executor state MAF persists to Cosmos, so resumability depends on it).
/// </summary>
public class SearchHistorySerializerTests
{
    [Fact]
    public void Snapshot_SerializeThenRestore_RoundTrips()
    {
        var context = WorkflowTestFactory.CreateContext();
        var pass = new LoopPass { Pass = 1, Query = "q1", QueryRationale = "because" };
        pass.Hits.Add(new SearchHit { Url = "https://gov.uk/a", SourceQuery = "q1", Rank = 1, Domain = "gov.uk" });

        var review = new Review
        {
            ThoughtProcess = "thought",
            LlmDecision = LoopDecision.Retry,
            FinalDecision = LoopDecision.Finalize,
            DecisionOverride = true,
            OverrideReason = "maxLoops",
        };
        review.Vetted.Add(WorkflowTestFactory.Item("https://gov.uk/a", Verdict.Relevant));
        review.Discarded.Add(WorkflowTestFactory.Item("https://gov.uk/c", Verdict.NotRelevant));
        pass.Review = review;

        context.History.Passes.Add(pass);
        context.History.ProcessedKeys.Add("gov.uk/a");

        var json = JsonSerializer.Serialize(SearchHistorySerializer.ToSnapshot(context.History));
        var snapshot = JsonSerializer.Deserialize<SearchHistorySnapshot>(json)!;

        var restored = new SearchHistory();
        SearchHistorySerializer.Restore(restored, snapshot);

        restored.Passes.Should().HaveCount(1);
        var restoredPass = restored.Passes[0];
        restoredPass.Query.Should().Be("q1");
        restoredPass.QueryRationale.Should().Be("because");
        restoredPass.Hits.Should().ContainSingle(h => h.Url == "https://gov.uk/a");
        restoredPass.Review.Should().NotBeNull();
        restoredPass.Review!.FinalDecision.Should().Be(LoopDecision.Finalize);
        restoredPass.Review.DecisionOverride.Should().BeTrue();
        restoredPass.Review.Vetted.Should().ContainSingle();
        restoredPass.Review.Discarded.Should().ContainSingle();
        restored.ProcessedKeys.Should().Contain("gov.uk/a");
    }
}
