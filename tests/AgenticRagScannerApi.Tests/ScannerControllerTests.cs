using AgenticRagScannerApi.Controllers;
using AgenticRagScannerApi.Models;
using AgenticRagScannerApi.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace AgenticRagScannerApi.Tests;

public class ScannerControllerTests
{
    [Fact]
    public void Scan_WhenModelStateIsInvalid_ReturnsBadRequestValidationProblem()
    {
        var controller = CreateController();
        controller.ModelState.AddModelError(nameof(ScanRequest.Jurisdiction), "Jurisdiction is required.");

        var request = new ScanRequest
        {
            AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Jurisdiction = string.Empty,
            TopicGroups = ["Tax"],
        };

        var result = controller.Scan(request);

        var badRequest = result.Should().BeOfType<ObjectResult>().Subject;
        var details = badRequest.Value.Should().BeOfType<ValidationProblemDetails>().Subject;
        details.Errors.Should().ContainKey(nameof(ScanRequest.Jurisdiction));
    }

    [Fact]
    public void Scan_WhenRequestIsValid_ReturnsAcceptedWithRunMetadata()
    {
        var controller = CreateController();
        var request = new ScanRequest
        {
            AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Jurisdiction = "United Kingdom",
            TopicGroups = ["Tax", "Conduct"],
        };

        var result = controller.Scan(request);

        var accepted = result.Should().BeOfType<AcceptedAtActionResult>().Subject;
        accepted.ActionName.Should().Be(nameof(ScannerController.Scan));
        accepted.RouteValues.Should().ContainKey("runId");

        var response = accepted.Value.Should().BeOfType<ScanResponse>().Subject;
        response.Status.Should().Be("Accepted");
        response.RunId.Should().MatchRegex("^[a-f0-9]{32}$");
        response.TopicGroups.Should().BeEquivalentTo(request.TopicGroups);
        response.AcceptedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    private static ScannerController CreateController()
    {
        return new ScannerController(
            Mock.Of<IFoundryService>(),
            Mock.Of<IAzureSearchService>(),
            Mock.Of<IAzureStorageService>(),
            Mock.Of<IBingSearchGroundingService>(),
            Mock.Of<IBingCustomSearchGroundingService>(),
            Mock.Of<ILogger<ScannerController>>());
    }
}