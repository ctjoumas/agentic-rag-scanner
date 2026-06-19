using System.ComponentModel.DataAnnotations;
using AgenticRagScannerApi.Configuration;
using FluentAssertions;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Verifies the DataAnnotations that drive `ValidateOnStart`: a misconfigured (empty / non-URL)
/// option is rejected, so the app fails fast at boot rather than on first use.
/// </summary>
public class OptionsValidationTests
{
    private static IReadOnlyList<ValidationResult> Validate(object options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void FoundryOptions_WhenFullyConfigured_ShouldBeValid()
    {
        var options = new FoundryOptions
        {
            Endpoint = "https://foundry.example.com",
            ModelDeploymentName = "gpt-4o",
        };

        Validate(options).Should().BeEmpty();
    }

    [Fact]
    public void FoundryOptions_WithEmptyEndpoint_ShouldFailRequired()
    {
        var options = new FoundryOptions
        {
            Endpoint = "",
            ModelDeploymentName = "gpt-4o",
        };

        Validate(options).Should().Contain(r => r.MemberNames.Contains(nameof(FoundryOptions.Endpoint)));
    }

    [Fact]
    public void FoundryOptions_WithNonUrlEndpoint_ShouldFailUrl()
    {
        var options = new FoundryOptions
        {
            Endpoint = "<your-foundry-endpoint>",  // the kind of placeholder that must NOT pass [Url]
            ModelDeploymentName = "gpt-4o",
        };

        Validate(options).Should().Contain(r => r.MemberNames.Contains(nameof(FoundryOptions.Endpoint)));
    }
}
