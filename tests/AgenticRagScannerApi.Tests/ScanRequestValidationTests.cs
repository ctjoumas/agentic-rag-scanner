using AgenticRagScannerApi.Models;
using AgenticRagScannerApi.Validators;
using FluentAssertions;

namespace AgenticRagScannerApi.Tests;

public class ScanRequestValidationTests
{
    [Fact]
    public void ScanRequestValidator_WithValidData_ShouldBeValid()
    {
        var validator = new ScanRequestValidator();
        var request = new ScanRequest
        {
            AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Jurisdiction = "United Kingdom",
            TopicGroups = ["Financial Conduct", "AML Controls"],
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ScanRequestValidator_WithoutTopicGroups_ShouldFailValidation()
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
        result.Errors.Should().Contain(r => r.ErrorMessage == "At least one topic group must be selected.");
    }

    [Fact]
    public void ScanRequestValidator_WithTooShortJurisdiction_ShouldFailValidation()
    {
        var validator = new ScanRequestValidator();
        var request = new ScanRequest
        {
            AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Jurisdiction = "U",
            TopicGroups = ["Tax"],
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(r => r.PropertyName == nameof(ScanRequest.Jurisdiction));
    }
}
