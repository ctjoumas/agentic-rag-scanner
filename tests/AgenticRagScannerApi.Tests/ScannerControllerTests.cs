using AgenticRagScannerApi.Controllers;
using AgenticRagScannerApi.Core.Runtime;
using AgenticRagScannerApi.Models;
using AgenticRagScannerApi.Orchestration;
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
    public async Task Scan_WhenCustomValidatorFails_ThrowsValidationException()
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

        var act = () => controller.Scan(request, CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<ValidationException>()).Which;
        exception.Errors.Should().Contain(e => e.PropertyName == nameof(ScanRequest.Jurisdiction));
    }

    [Fact]
    public async Task Scan_WhenRequestIsValid_ReturnsOkWithAggregatedResults()
    {
        var request = new ScanRequest
        {
            AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Jurisdiction = "United Kingdom",
            TopicGroups = ["Tax", "Conduct"],
        };

        var expected = new ScanResult
        {
            RunId = "abc123",
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Groups =
            [
                new TopicGroupResult { GroupId = "tax", GroupName = "Tax", Status = "Completed" },
                new TopicGroupResult { GroupId = "conduct", GroupName = "Conduct", Status = "Completed" },
            ],
        };

        var orchestrator = new Mock<IScanOrchestrator>();
        orchestrator
            .Setup(o => o.RunAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var controller = CreateController(orchestrator.Object);

        var result = await controller.Scan(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        ok.Value.Should().BeSameAs(expected);
        orchestrator.Verify(o => o.RunAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ScannerController CreateController(IValidator<ScanRequest> validator) =>
        CreateController(validator, Mock.Of<IScanOrchestrator>());

    private static ScannerController CreateController(IScanOrchestrator orchestrator) =>
        CreateController(CreatePassingValidator(), orchestrator);

    private static ScannerController CreateController(
        IValidator<ScanRequest> validator,
        IScanOrchestrator orchestrator) =>
        new(orchestrator, validator, Mock.Of<ILogger<ScannerController>>());

    private static IValidator<ScanRequest> CreatePassingValidator()
    {
        var validator = new Mock<IValidator<ScanRequest>>();
        validator
            .Setup(v => v.Validate(It.IsAny<ScanRequest>()))
            .Returns(new ValidationResult());
        return validator.Object;
    }
}