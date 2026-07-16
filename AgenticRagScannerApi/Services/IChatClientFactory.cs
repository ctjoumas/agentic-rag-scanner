using AgenticRagScannerApi.Configuration;

namespace AgenticRagScannerApi.Services;

/// <summary>
/// Builds (and caches) the resilient Foundry <see cref="Microsoft.Extensions.AI.IChatClient"/> for a given
/// model deployment. Every agent shares the same Foundry endpoint (a single underlying Azure OpenAI
/// client); only the model deployment differs, so agents can be configured to run on different models
/// while pointing at the same Foundry project.
/// </summary>
public interface IChatClientFactory
{
    /// <summary>
    /// Returns a resilient <see cref="Microsoft.Extensions.AI.IChatClient"/> bound to the given model
    /// deployment. Instances are cached per deployment name (case-insensitive).
    /// </summary>
    Microsoft.Extensions.AI.IChatClient Create(string modelDeploymentName);
}
