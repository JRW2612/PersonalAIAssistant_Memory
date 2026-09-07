using System.Text.RegularExpressions;
using FluentValidation;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;

namespace PersonalAIAssistant.Memory.Business.Validators;

public class AddMemoryCommandValidator : AbstractValidator<AddMemoryCommand>
{
    private static readonly Regex UnsafeUrlRegex = new(
        @"\b(?:https?|ftp|file|gopher|dict|ldap)://(?:127\.0\.0\.1|169\.254\.169\.254|0\.0\.0\.0|localhost|metadata\.google\.internal|instance-data|(?:10\.\d{1,3}|192\.168\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01]))\.\d{1,3})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public AddMemoryCommandValidator(IUrlSafetyValidator? urlSafetyValidator = null)
    {
        RuleFor(x => x.RawText)
            .NotEmpty().WithMessage("Memory text must not be empty.")
            .MaximumLength(10_000).WithMessage("Memory text must not exceed 10,000 characters.");

        RuleFor(x => x.RawText)
            .Must(text =>
            {
                if (string.IsNullOrWhiteSpace(text)) return true;
                if (urlSafetyValidator != null)
                {
                    return !urlSafetyValidator.ScanText(text).HasUnsafeUrl;
                }
                return !UnsafeUrlRegex.IsMatch(text);
            })
            .WithMessage("Memory text contains a potentially unsafe internal or metadata URL.");

        RuleFor(x => x.Source)
            .NotEmpty().WithMessage("Source must not be empty.");

        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId must not be empty.");

        RuleFor(x => x.Tags)
            .NotNull().WithMessage("Tags collection must not be null.");
    }
}
