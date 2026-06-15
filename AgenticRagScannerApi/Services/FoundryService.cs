using AgenticRagScannerApi.Configuration;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Services;

/// <inheritdoc />
public class FoundryService : IFoundryService
{
    private readonly FoundryOptions _options;
    private readonly ILogger<FoundryService> _logger;

    public FoundryService(IOptions<FoundryOptions> options, ILogger<FoundryService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default)
    {
        // TODO: implement against Microsoft Foundry (prefer DefaultAzureCredential).
        throw new NotImplementedException();
    }
}
