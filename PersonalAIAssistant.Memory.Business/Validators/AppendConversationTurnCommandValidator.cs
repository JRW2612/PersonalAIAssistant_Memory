// PersonalAIAssistant.Memory.Business/Validators/AppendConversationTurnCommandValidator.cs
using FluentValidation;
using PersonalAIAssistant.Memory.Business.Commands;

namespace PersonalAIAssistant.Memory.Business.Validators;

public class AppendConversationTurnCommandValidator : AbstractValidator<AppendConversationTurnCommand>
{
    private static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "user", "assistant", "system"
    };

    public AppendConversationTurnCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId must not be empty.")
            .MaximumLength(128).WithMessage("UserId must not exceed 128 characters.");

        RuleFor(x => x.SessionId)
            .NotEmpty().WithMessage("SessionId must not be empty.")
            .MaximumLength(128).WithMessage("SessionId must not exceed 128 characters.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role must not be empty.")
            .Must(role => AllowedRoles.Contains(role))
            .WithMessage("Role must be 'user', 'assistant', or 'system'.");

        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Turn content must not be empty.")
            .MaximumLength(10_000).WithMessage("Turn content must not exceed 10,000 characters.");
    }
}
