using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Workflows.Agents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Stories 2.5-2.9 - the stub agents return canned, schema-valid output (no LLM calls).
/// </summary>
public class StubAgentsTests
{
    [Fact]
    public async Task QuerySynthesis_ReturnsNonEmptyQuery()
    {
        var context = WorkflowTestFactory.CreateContext();
        var agent = new QuerySynthesisAgentStub(NullLogger<QuerySynthesisAgentStub>.Instance);

        var result = await agent.SynthesizeAsync(context);

        result.Query.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RelevanceEval_ReturnsOneVerdictPerDocument()
    {
        var context = WorkflowTestFactory.CreateContext();
        var agent = new RelevanceEvalAgentStub(NullLogger<RelevanceEvalAgentStub>.Instance);
        var documents = new[]
        {
            WorkflowTestFactory.Doc("https://gov.uk/a"),
            WorkflowTestFactory.Doc("https://gov.uk/b"),
        };

        var decision = await agent.EvaluateAsync(context, documents);

        decision.Items.Should().HaveCount(2);
        decision.Items.Select(i => i.Index).Should().BeEquivalentTo(new[] { 0, 1 });
        decision.ThoughtProcess.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EnrichCategorizeSummarize_PopulateTheirFields()
    {
        var context = WorkflowTestFactory.CreateContext();
        var item = WorkflowTestFactory.Item("https://gov.uk/a", Verdict.Relevant);

        await new EnrichmentAgentStub(NullLogger<EnrichmentAgentStub>.Instance).EnrichAsync(item, context);
        await new CategorizeAgentStub(NullLogger<CategorizeAgentStub>.Instance).CategorizeAsync(item);
        await new SummarizeImpactAgentStub(NullLogger<SummarizeImpactAgentStub>.Instance).SummarizeAsync(item, context);

        item.WhatItDoes.Should().NotBeNullOrWhiteSpace();
        item.ImpactArea.Should().NotBeNullOrWhiteSpace();
        item.Regulator.Should().NotBeNullOrWhiteSpace();
        item.Tags.Should().NotBeEmpty();
        item.ImpactSummary.Should().NotBeNullOrWhiteSpace();
    }
}
