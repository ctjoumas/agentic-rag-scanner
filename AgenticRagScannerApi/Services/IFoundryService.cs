namespace AgenticRagScannerApi.Services;

/// <summary>
/// Abstraction over Microsoft Foundry — hosts the models used for all LLM calls
/// (query synthesis, relevance eval, categorize, summarize).
/// </summary>
public interface IFoundryService
{
    /// <summary>Sends a prompt to the configured model deployment and returns the completion.</summary>
    Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default);
}
