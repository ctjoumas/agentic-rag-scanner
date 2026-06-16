using AgenticRagScannerApi.Models;
using FluentValidation;

namespace AgenticRagScannerApi.Validators;

public class ScanRequestValidator : AbstractValidator<ScanRequest>
{
    public ScanRequestValidator()
    {
        RuleFor(x => x.AsOfDate)
            .NotNull();

        RuleFor(x => x.Jurisdiction)
            .NotEmpty()
            .Length(2, 100);

        RuleFor(x => x.TopicGroups)
            .NotNull()
            .Must(t => t is { Count: > 0 })
            .WithMessage("At least one topic group must be selected.");

        RuleForEach(x => x.TopicGroups)
            .NotEmpty();
    }
}