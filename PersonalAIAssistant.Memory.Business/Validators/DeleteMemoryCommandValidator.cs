using FluentValidation;
using PersonalAIAssistant.Memory.Business.Commands;

namespace PersonalAIAssistant.Memory.Business.Validators
{
    public class DeleteMemoryCommandValidator : AbstractValidator<DeleteMemoryCommand>
    {
        public DeleteMemoryCommandValidator()
        {
            RuleFor(x => x.MemoryId)
                .NotEmpty().WithMessage("MemoryId must not be empty.");

            RuleFor(x => x.Reason)
                .NotEmpty().WithMessage("A deletion reason must be provided.")
                .MaximumLength(500).WithMessage("Reason must not exceed 500 characters.");

            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("UserId must not be empty.");
        }
    }
}
