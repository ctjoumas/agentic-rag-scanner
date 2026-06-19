namespace AgenticRagScannerApi.Services;

/// <summary>
/// Convenience facade over the shared Foundry <c>IChatClient</c> for simple
/// prompt-in / text-out completions. The MAF agents reference the <c>IChatClient</c> directly;
/// this service is a thin wrapper for non-agent callers.
/// </summary>
public interface IFoundryService
{
    /// <summary>Sends a prompt to the configured model deployment and returns the completion text.</summary>
    Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default);
}
