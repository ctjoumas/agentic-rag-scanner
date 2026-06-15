using AgenticRagScannerApi.Configuration;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Services;

/// <inheritdoc />
public class AzureSearchService : IAzureSearchService
{
    private readonly AzureSearchOptions _options;
    private readonly ILogger<AzureSearchService> _logger;

    public AzureSearchService(IOptions<AzureSearchOptions> options, ILogger<AzureSearchService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<string>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        // TODO: implement using Azure.Search.Documents (prefer DefaultAzureCredential).
        throw new NotImplementedException();
    }
}
