using FluentValidation;
using PersonalAIAssistant.Memory.Business.Commands;

namespace PersonalAIAssistant.Memory.Business.Validators
{
    public class UpdateMemoryCommandValidator : AbstractValidator<UpdateMemoryCommand>
    {
        public UpdateMemoryCommandValidator()
        {
            RuleFor(x => x.MemoryId)
                .NotEmpty().WithMessage("MemoryId must not be empty.");

            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("UserId must not be empty.");

            RuleFor(x => x.UpdatedFields)
                .NotNull().WithMessage("UpdatedFields must not be null.")
                .Must(fields => fields != null && fields.Count > 0)
                .WithMessage("At least one field must be specified for update.");
        }
    }
}
