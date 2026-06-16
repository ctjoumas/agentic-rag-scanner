using System.ComponentModel.DataAnnotations;
using AgenticRagScannerApi.Models;
using FluentAssertions;

namespace AgenticRagScannerApi.Tests;

public class ScanRequestValidationTests
{
    [Fact]
    public void ScanRequest_WithValidData_ShouldBeValid()
    {
        var request = new ScanRequest
        {
            AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Jurisdiction = "United Kingdom",
            TopicGroups = ["Financial Conduct", "AML Controls"],
        };

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void ScanRequest_WithoutTopicGroups_ShouldFailValidation()
    {
        var request = new ScanRequest
        {
            AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Jurisdiction = "United Kingdom",
            TopicGroups = [],
        };

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.ErrorMessage == "At least one topic group must be selected.");
    }

    [Fact]
    public void ScanRequest_WithTooShortJurisdiction_ShouldFailValidation()
    {
        var request = new ScanRequest
        {
            AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Jurisdiction = "U",
            TopicGroups = ["Tax"],
        };

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(ScanRequest.Jurisdiction)));
    }

    private static bool TryValidate(ScanRequest request, out List<ValidationResult> results)
    {
        var context = new ValidationContext(request);
        results = [];
        return Validator.TryValidateObject(request, context, results, validateAllProperties: true);
    }
}
