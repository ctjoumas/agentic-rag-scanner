using Microsoft.Extensions.AI;

namespace AgenticRagScannerApi.Services;

/// <inheritdoc />
public class FoundryService : IFoundryService
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<FoundryService> _logger;

    public FoundryService(IChatClient chatClient, ILogger<FoundryService> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Foundry completion requested ({PromptLength} chars).", prompt.Length);

        var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);
        return response.Text;
    }
}
