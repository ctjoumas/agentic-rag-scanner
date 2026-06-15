using AgenticRagScannerApi.Models;
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

    /// <summary>
    /// Manually triggers a horizon scan for the supplied date + jurisdiction +
    /// topic groups. For this sprint the request is validated and acknowledged
    /// with a run id; the per-topic-group MAF workflow orchestration is implemented later.
    /// </summary>
    [HttpPost("scan")]
    [ProducesResponseType(typeof(ScanResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Scan([FromBody] ScanRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var runId = Guid.NewGuid().ToString("N");

        _logger.LogInformation(
            "Accepted scan run {RunId}: jurisdiction={Jurisdiction}, asOfDate={AsOfDate}, topicGroups={TopicGroupCount}",
            runId, request.Jurisdiction, request.AsOfDate, request.TopicGroups.Count);

        // TODO: fan out one MAF workflow per topic group under a shared throttle
        // (architecture-context.md §3). Deferred per §5.
        var response = new ScanResponse
        {
            RunId = runId,
            TopicGroups = request.TopicGroups,
        };

        return AcceptedAtAction(nameof(Scan), new { runId }, response);
    }
}
