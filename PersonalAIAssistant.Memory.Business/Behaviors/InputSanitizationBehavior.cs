using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Business.Behaviors;

/// <summary>
/// MediatR pipeline behavior that validates and sanitizes input payloads.
/// Defends against oversized payloads, null-byte injection, and adversarial Unicode direction overrides.
/// SRP: strictly responsible for input structure hygiene before domain processing.
/// </summary>
public sealed class InputSanitizationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly AiThreatOptions _options;
    private readonly ISecurityAuditLogger _auditLogger;
    private readonly ILogger<InputSanitizationBehavior<TRequest, TResponse>> _logger;

    // Unicode directional formatting characters often abused in adversarial prompts
    private static readonly char[] DangerousUnicodeChars =
    {
        '\u202A', '\u202B', '\u202C', '\u202D', '\u202E', // LRE, RLE, PDF, LRO, RLO
        '\u2066', '\u2067', '\u2068', '\u2069'              // LRI, RLI, FSI, PDI
    };

    public InputSanitizationBehavior(
        IOptions<AiThreatOptions> options,
        ISecurityAuditLogger auditLogger,
        ILogger<InputSanitizationBehavior<TRequest, TResponse>> logger)
    {
        _options = options.Value;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_options.EnableInputSanitization)
        {
            return await next(cancellationToken);
        }

        var text = ExtractText(request);
        if (text is not null)
        {
            var userId = ExtractUserId(request);

            // 1. Length validation to prevent memory exhaustion / DoS
            if (text.Length > _options.MaxInputLengthChars)
            {
                _auditLogger.LogSuspiciousInput(userId, typeof(TRequest).Name, $"Payload exceeds {_options.MaxInputLengthChars} characters.");
                throw new InputSanitizationException("PayloadTooLarge", $"Memory text exceeds maximum permitted length of {_options.MaxInputLengthChars} characters.");
            }

            // 2. Null byte detection
            if (text.IndexOf('\0') >= 0)
            {
                _auditLogger.LogSuspiciousInput(userId, typeof(TRequest).Name, "Null byte (\\0) detected in memory input.");
                throw new InputSanitizationException("NullByteDetected", "Input contains illegal null byte characters.");
            }

            // 3. Dangerous Unicode bidirectional override detection
            if (text.IndexOfAny(DangerousUnicodeChars) >= 0)
            {
                _auditLogger.LogSuspiciousInput(userId, typeof(TRequest).Name, "Adversarial Unicode bidirectional formatting character detected.");
                throw new InputSanitizationException("AdversarialUnicodeDetected", "Input contains prohibited Unicode direction-override control characters.");
            }
        }

        return await next(cancellationToken);
    }

    private static string? ExtractText(TRequest request) => request switch
    {
        AddMemoryCommand cmd => cmd.RawText,
        UpdateMemoryCommand cmd => cmd.UpdatedFields?.GetValueOrDefault("RawText"),
        ConsolidateMemoriesCommand cmd => cmd.ConsolidatedText,
        _ => null
    };

    private static string ExtractUserId(TRequest request) => request switch
    {
        AddMemoryCommand cmd => cmd.UserId,
        UpdateMemoryCommand cmd => cmd.UserId,
        ConsolidateMemoriesCommand cmd => cmd.UserId,
        _ => "unknown"
    };
}
