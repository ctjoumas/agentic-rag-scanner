using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AgenticRagScannerApi.Diagnostics;

/// <summary>
/// Central <see cref="ActivitySource"/> and <see cref="Meter"/> for the scan orchestrator's parallel
/// fan-out (Epic 13, story 13.2). Exposes:
/// <list type="bullet">
/// <item>a per-topic-group <em>span</em> (<see cref="StartTopicGroupActivity"/>) tagged with run/group ids so
/// concurrent groups are visible as parallel traces,</item>
/// <item>an <em>in-flight concurrency</em> gauge (how many topic-group workflows are executing right now),</item>
/// <item>a <em>worker-gate wait-time</em> histogram (how long a group queued for a worker slot - the
/// backpressure signal when the parallelism cap is saturated), and</item>
/// <item>a <em>group-outcome</em> counter partitioned by terminal status.</item>
/// </list>
/// The instrument/source names are registered with OpenTelemetry in
/// <c>ServiceCollectionExtensions.AddScannerObservability</c> so the measurements are exported.
/// </summary>
public static class ScannerDiagnostics
{
    /// <summary>Name of the orchestration <see cref="ActivitySource"/> (register with OpenTelemetry tracing).</summary>
    public const string ActivitySourceName = "AgenticRagScanner.Orchestration";

    /// <summary>Name of the orchestration <see cref="Meter"/> (register with OpenTelemetry metrics).</summary>
    public const string MeterName = "AgenticRagScanner.Orchestration";

    private static readonly ActivitySource s_activitySource = new(ActivitySourceName);
    private static readonly Meter s_meter = new(MeterName);

    private static int s_inFlightGroups;

    /// <summary>Number of topic-group workflows executing at this instant (across all in-progress runs).</summary>
    private static readonly ObservableGauge<int> s_inFlightGauge = s_meter.CreateObservableGauge(
        "scanner.topic_groups.in_flight",
        () => Volatile.Read(ref s_inFlightGroups),
        unit: "{group}",
        description: "Topic-group workflows currently executing in parallel.");

    private static readonly Histogram<double> s_workerWaitMs = s_meter.CreateHistogram<double>(
        "scanner.topic_group.worker_wait",
        unit: "ms",
        description: "Time a topic group waited for a parallel-execution worker slot (backpressure).");

    private static readonly Counter<long> s_groupOutcomes = s_meter.CreateCounter<long>(
        "scanner.topic_groups.completed",
        unit: "{group}",
        description: "Topic-group workflows that finished, partitioned by terminal status.");

    /// <summary>Starts a span for one topic group's execution, tagged with the run and group identity.</summary>
    public static Activity? StartTopicGroupActivity(string runId, string groupId, string groupName)
    {
        var activity = s_activitySource.StartActivity("topic_group.execute", ActivityKind.Internal);
        activity?.SetTag("scanner.run_id", runId);
        activity?.SetTag("scanner.topic_group_id", groupId);
        activity?.SetTag("scanner.topic_group_name", groupName);
        return activity;
    }

    /// <summary>Marks a topic-group workflow as started (increments the in-flight gauge).</summary>
    public static void GroupStarted() => Interlocked.Increment(ref s_inFlightGroups);

    /// <summary>Marks a topic-group workflow as finished (decrements the in-flight gauge).</summary>
    public static void GroupFinished() => Interlocked.Decrement(ref s_inFlightGroups);

    /// <summary>Records how long a group waited for a worker slot before it began executing.</summary>
    public static void RecordWorkerWait(double milliseconds, string groupId) =>
        s_workerWaitMs.Record(milliseconds, new KeyValuePair<string, object?>("scanner.topic_group_id", groupId));

    /// <summary>Counts a finished topic group by its terminal status (e.g. "Completed" / "Failed").</summary>
    public static void RecordGroupOutcome(string status) =>
        s_groupOutcomes.Add(1, new KeyValuePair<string, object?>("scanner.status", status));
}
