using FluentValidation;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains.Enums;

namespace PersonalAIAssistant.Memory.Business.Validators
{
    public class AddMemoryCommandValidator : AbstractValidator<AddMemoryCommand>
    {
        public AddMemoryCommandValidator()
        {
            RuleFor(x => x.RawText)
                .NotEmpty().WithMessage("Memory text must not be empty.")
                .MaximumLength(10_000).WithMessage("Memory text must not exceed 10,000 characters.");

            RuleFor(x => x.Source)
                .NotEmpty().WithMessage("Source must not be empty.")
                .Must(s => Enum.TryParse<MemorySource>(s, ignoreCase: true, out var src) && src != MemorySource.Unknown)
                .WithMessage("Source must be a known value: Chat, Email, Note, System, User, or Other.");

            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("UserId must not be empty.");

            RuleFor(x => x.Tags)
                .NotNull().WithMessage("Tags collection must not be null.");
        }
    }
}
