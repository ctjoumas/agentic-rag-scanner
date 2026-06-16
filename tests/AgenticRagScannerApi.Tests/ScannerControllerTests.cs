using AgenticRagScannerApi.Controllers;
using AgenticRagScannerApi.Mappers;
using AgenticRagScannerApi.Models;
using AgenticRagScannerApi.Services;
using FluentValidation;
using FluentValidation.Results;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace AgenticRagScannerApi.Tests;

public class ScannerControllerTests
{
    [Fact]
    public void Scan_WhenCustomValidatorFails_ThrowsValidationException()
    {
        var validator = new Mock<IValidator<ScanRequest>>();
        validator
            .Setup(v => v.Validate(It.IsAny<ScanRequest>()))
            .Returns(new ValidationResult(
            [
                new ValidationFailure(nameof(ScanRequest.Jurisdiction), "Jurisdiction is required.")
            ]));

        var controller = CreateController(validator.Object);
        var request = new ScanRequest
        {
            AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Jurisdiction = string.Empty,
            TopicGroups = ["Tax"],
        };

        var act = () => controller.Scan(request);

        var exception = act.Should().Throw<ValidationException>().Which;
        exception.Errors.Should().Contain(e => e.PropertyName == nameof(ScanRequest.Jurisdiction));
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
        var validator = new Mock<IValidator<ScanRequest>>();
        validator
            .Setup(v => v.Validate(It.IsAny<ScanRequest>()))
            .Returns(new ValidationResult());

        return CreateController(validator.Object);
    }

    private static ScannerController CreateController(IValidator<ScanRequest> scanRequestValidator)
    {
        return new ScannerController(
            Mock.Of<IFoundryService>(),
            Mock.Of<IAzureSearchService>(),
            Mock.Of<IAzureStorageService>(),
            Mock.Of<IBingSearchGroundingService>(),
            Mock.Of<IBingCustomSearchGroundingService>(),
            new ScanMapper(),
            scanRequestValidator,
            Mock.Of<ILogger<ScannerController>>());
    }
}