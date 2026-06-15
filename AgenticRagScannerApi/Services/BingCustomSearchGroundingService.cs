using AgenticRagScannerApi.Configuration;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Services;

/// <inheritdoc />
public class BingCustomSearchGroundingService : IBingCustomSearchGroundingService
{
    private readonly BingCustomSearchGroundingOptions _options;
    private readonly ILogger<BingCustomSearchGroundingService> _logger;

    public BingCustomSearchGroundingService(IOptions<BingCustomSearchGroundingOptions> options, ILogger<BingCustomSearchGroundingService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<string>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        // TODO: implement Grounding with Bing Custom Search via Foundry.
        throw new NotImplementedException();
    }
}
