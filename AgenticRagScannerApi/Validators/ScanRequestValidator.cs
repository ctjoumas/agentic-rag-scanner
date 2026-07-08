using AgenticRagScannerApi.Models;
using FluentValidation;

namespace AgenticRagScannerApi.Validators;

public class ScanRequestValidator : AbstractValidator<ScanRequest>
{
    public ScanRequestValidator()
    {
        // Both dates are optional. When both are supplied, the window must be well-formed.
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate!.Value)
            .When(x => x.StartDate is not null && x.EndDate is not null)
            .WithMessage("endDate must be on or after startDate.");

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