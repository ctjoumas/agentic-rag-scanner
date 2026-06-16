using AgenticRagScannerApi.Mappers;
using AgenticRagScannerApi.Models;
using AgenticRagScannerApi.Validators;
using FluentAssertions;

namespace AgenticRagScannerApi.Tests;

public class ValidationAndMappingTests
{
    [Fact]
    public void ScanRequestValidator_WhenTopicGroupsEmpty_ShouldReturnValidationError()
    {
        var validator = new ScanRequestValidator();
        var request = new ScanRequest
        {
            AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Jurisdiction = "United Kingdom",
            TopicGroups = [],
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "At least one topic group must be selected.");
    }

    [Fact]
    public void ScanMapper_ToResponse_ShouldMapRunContextAndTopicGroups()
    {
        var mapper = new ScanMapper();
        var request = new ScanRequest
        {
            AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Jurisdiction = "United Kingdom",
            TopicGroups = ["Tax", "Conduct"],
        };

        const string runId = "abc123";
        var acceptedAtUtc = DateTimeOffset.UtcNow;

        var response = mapper.ToResponse(request, runId, acceptedAtUtc);

        response.RunId.Should().Be(runId);
        response.AcceptedAtUtc.Should().Be(acceptedAtUtc);
        response.Status.Should().Be("Accepted");
        response.TopicGroups.Should().BeEquivalentTo(request.TopicGroups);
    }
}