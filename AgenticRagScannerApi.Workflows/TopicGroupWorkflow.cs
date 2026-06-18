using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Workflows.Pipeline;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace AgenticRagScannerApi.Workflows;

/// <summary>
/// Builds the MAF workflow for a single topic group: a self-looping <see cref="TopicGroupLoopExecutor"/>
/// that runs one pass per super-step and yields a <see cref="TopicGroupResult"/> when the loop exits.
/// One workflow instance is built per topic group (per the design - parallel fan-out is Epic 12).
/// </summary>
public static class TopicGroupWorkflow
{
    /// <summary>The signal that starts the loop (pass it as the workflow input).</summary>
    public const PassSignal StartSignal = PassSignal.Start;

    /// <summary>Builds the per-group workflow over the supplied context and pipeline.</summary>
    public static Workflow Build(TopicGroupContext context, TopicGroupPipeline pipeline, ILoggerFactory loggerFactory)
    {
        var executor = new TopicGroupLoopExecutor(
            context,
            pipeline,
            loggerFactory.CreateLogger<TopicGroupLoopExecutor>());

        return new WorkflowBuilder(executor)
            .AddEdge(executor, executor)   // self-loop: PassSignal.Continue drives the next pass
            .WithOutputFrom(executor)
            .Build();
    }
}
