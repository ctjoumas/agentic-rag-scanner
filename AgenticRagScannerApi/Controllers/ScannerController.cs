using AgenticRagScannerApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgenticRagScannerApi.Controllers;

/// <summary>
/// Entry point for triggering a horizon scan. For the sprint this is a manual
/// trigger; the orchestration of per-topic-group workflows is implemented later.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class ScannerController : ControllerBase
{
    private readonly IFoundryService _foundryService;
    private readonly IAzureSearchService _searchService;
    private readonly IAzureStorageService _storageService;
    private readonly IBingSearchGroundingService _bingSearchGroundingService;
    private readonly IBingCustomSearchGroundingService _bingCustomSearchGroundingService;
    private readonly ILogger<ScannerController> _logger;

    public ScannerController(
        IFoundryService foundryService,
        IAzureSearchService searchService,
        IAzureStorageService storageService,
        IBingSearchGroundingService bingSearchGroundingService,
        IBingCustomSearchGroundingService bingCustomSearchGroundingService,
        ILogger<ScannerController> logger)
    {
        _foundryService = foundryService;
        _searchService = searchService;
        _storageService = storageService;
        _bingSearchGroundingService = bingSearchGroundingService;
        _bingCustomSearchGroundingService = bingCustomSearchGroundingService;
        _logger = logger;
    }

    /// <summary>Placeholder scan endpoint. Implementation to follow.</summary>
    [HttpPost("scan")]
    public IActionResult Scan()
    {
        // TODO: orchestrate the per-topic-group MAF workflows.
        return StatusCode(StatusCodes.Status501NotImplemented, "Not implemented yet.");
    }
}
