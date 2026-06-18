using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Models;

namespace AgenticRagScannerApi.Orchestration;

/// <summary>
/// Drives the synchronous scan run lifecycle: maps a <see cref="ScanRequest"/> to per-topic-group
/// contexts, runs them sequentially (parallel fan-out is deferred to Epic 12), and returns the
/// aggregated <see cref="ScanResult"/> in a single call.
/// </summary>
public interface IScanOrchestrator
{
    /// <summary>
    /// Runs the scan described by <paramref name="request"/> to completion and returns its result.
    /// </summary>
    Task<ScanResult> RunAsync(ScanRequest request, CancellationToken cancellationToken = default);
}
