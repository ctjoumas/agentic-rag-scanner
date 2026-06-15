using AgenticRagScannerApi.Configuration;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Services;

/// <inheritdoc />
public class BingSearchGroundingService : IBingSearchGroundingService
{
    private readonly BingSearchGroundingOptions _options;
    private readonly ILogger<BingSearchGroundingService> _logger;

    public BingSearchGroundingService(IOptions<BingSearchGroundingOptions> options, ILogger<BingSearchGroundingService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<string>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        // TODO: implement Grounding with Bing Search via Foundry.
        throw new NotImplementedException();
    }
}
