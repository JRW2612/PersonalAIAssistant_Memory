using FluentValidation;
using PersonalAIAssistant.Memory.Business.Commands;

namespace PersonalAIAssistant.Memory.Business.Validators
{
    public class MemoryIndexedCommandValidator : AbstractValidator<MemoryIndexedCommand>
    {
        public MemoryIndexedCommandValidator()
        {
            RuleFor(x => x.MemoryId)
                .NotEmpty().WithMessage("MemoryId must not be empty.");

            RuleFor(x => x.EmbeddingId)
                .NotEmpty().WithMessage("EmbeddingId must not be empty.");

            RuleFor(x => x.VectorProvider)
                .NotEmpty().WithMessage("VectorProvider must not be empty.");
        }
    }
}
