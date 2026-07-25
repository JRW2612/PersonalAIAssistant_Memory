using FluentValidation;
using PersonalAIAssistant.Memory.Business.Commands;

namespace PersonalAIAssistant.Memory.Business.Validators
{
    public class ConsolidateMemoriesCommandValidator : AbstractValidator<ConsolidateMemoriesCommand>
    {
        public ConsolidateMemoriesCommandValidator()
        {
            RuleFor(x => x.ConsolidatedText)
                .NotEmpty().WithMessage("ConsolidatedText must not be empty.")
                .MaximumLength(20_000).WithMessage("ConsolidatedText must not exceed 20,000 characters.");

            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("UserId must not be empty.");

            RuleFor(x => x.MergedMemoryIds)
                .NotNull().WithMessage("MergedMemoryIds must not be null.")
                .Must(ids => ids != null && ids.Count >= 2)
                .WithMessage("At least 2 memory IDs must be provided for consolidation.");
        }
    }
}
