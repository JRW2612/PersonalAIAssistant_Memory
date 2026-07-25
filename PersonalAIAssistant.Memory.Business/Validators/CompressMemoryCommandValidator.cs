using FluentValidation;
using PersonalAIAssistant.Memory.Business.Commands;

namespace PersonalAIAssistant.Memory.Business.Validators
{
    public class CompressMemoryCommandValidator : AbstractValidator<CompressMemoryCommand>
    {
        public CompressMemoryCommandValidator()
        {
            RuleFor(x => x.OriginalMemoryId)
                .NotEmpty().WithMessage("OriginalMemoryId must not be empty.");

            RuleFor(x => x.CompressedText)
                .NotEmpty().WithMessage("CompressedText must not be empty.")
                .MaximumLength(10_000).WithMessage("CompressedText must not exceed 10,000 characters.");

            RuleFor(x => x.CompressionModel)
                .NotEmpty().WithMessage("CompressionModel must not be empty.");

            RuleFor(x => x.TokenCount)
                .GreaterThan(0).WithMessage("TokenCount must be a positive integer.");

            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("UserId must not be empty.");
        }
    }
}
