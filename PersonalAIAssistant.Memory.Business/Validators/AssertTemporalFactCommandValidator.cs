using FluentValidation;
using PersonalAIAssistant.Memory.Business.Commands;

namespace PersonalAIAssistant.Memory.Business.Validators;

public class AssertTemporalFactCommandValidator : AbstractValidator<AssertTemporalFactCommand>
{
    public AssertTemporalFactCommandValidator()
    {
        RuleFor(x => x.Subject)
            .NotEmpty().WithMessage("Subject must not be empty.")
            .MaximumLength(256).WithMessage("Subject must not exceed 256 characters.");

        RuleFor(x => x.Predicate)
            .NotEmpty().WithMessage("Predicate must not be empty.")
            .MaximumLength(128).WithMessage("Predicate must not exceed 128 characters.");

        RuleFor(x => x.Object)
            .NotEmpty().WithMessage("Object must not be empty.")
            .MaximumLength(1000).WithMessage("Object must not exceed 1000 characters.");

        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId must not be empty.")
            .MaximumLength(128).WithMessage("UserId must not exceed 128 characters.");

        RuleFor(x => x.Confidence)
            .InclusiveBetween(0.0, 1.0).WithMessage("Confidence must be between 0.0 and 1.0.");
    }
}
