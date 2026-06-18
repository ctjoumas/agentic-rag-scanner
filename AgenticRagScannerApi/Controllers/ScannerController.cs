using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Models;
using AgenticRagScannerApi.Orchestration;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace AgenticRagScannerApi.Controllers;

/// <summary>
/// Entry point for triggering a horizon scan. The request is validated, then run synchronously:
/// each selected topic group is scanned sequentially and the aggregated results are returned in
/// the response (no run-status polling for the POC).
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class ScannerController : ControllerBase
{
    private readonly IScanOrchestrator _scanOrchestrator;
    private readonly IValidator<ScanRequest> _scanRequestValidator;
    private readonly ILogger<ScannerController> _logger;

    public ScannerController(
        IScanOrchestrator scanOrchestrator,
        IValidator<ScanRequest> scanRequestValidator,
        ILogger<ScannerController> logger)
    {
        _scanOrchestrator = scanOrchestrator;
        _scanRequestValidator = scanRequestValidator;
        _logger = logger;
    }

    /// <summary>
    /// Manually triggers a horizon scan for the supplied date + jurisdiction + topic groups.
    /// The scan runs synchronously and returns the aggregated per-topic-group results (200).
    /// </summary>
    [HttpPost("scan")]
    [ProducesResponseType(typeof(ScanResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Scan([FromBody] ScanRequest request, CancellationToken cancellationToken)
    {
        var validationResult = _scanRequestValidator.Validate(request);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        _logger.LogInformation(
            "Scan requested: jurisdiction={Jurisdiction}, asOfDate={AsOfDate}, topicGroups={TopicGroupCount}",
            request.Jurisdiction, request.AsOfDate, request.TopicGroups.Count);

        var result = await _scanOrchestrator.RunAsync(request, cancellationToken);

        return Ok(result);
    }
}