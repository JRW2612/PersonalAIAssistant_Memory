using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Business.Behaviors;

/// <summary>
/// MediatR pipeline behavior that scans memory write payloads for prompt injection, jailbreaks,
/// and adversarial system override attempts.
/// Defends against autonomous exploit agents embedding override instructions into long-term context.
/// </summary>
public sealed class PromptInjectionScanningBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IPromptInjectionDetector _detector;
    private readonly AiThreatOptions _options;
    private readonly ISecurityAuditLogger _auditLogger;
    private readonly ILogger<PromptInjectionScanningBehavior<TRequest, TResponse>> _logger;

    public PromptInjectionScanningBehavior(
        IPromptInjectionDetector detector,
        IOptions<AiThreatOptions> options,
        ISecurityAuditLogger auditLogger,
        ILogger<PromptInjectionScanningBehavior<TRequest, TResponse>> logger)
    {
        _detector = detector;
        _options = options.Value;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_options.EnablePromptInjectionDetection)
        {
            return await next(cancellationToken);
        }

        var text = ExtractText(request);
        if (text is not null)
        {
            var result = _detector.Scan(text);
            if (result.IsInjection)
            {
                var userId = ExtractUserId(request);
                _auditLogger.LogPromptInjectionAttempt(
                    userId,
                    typeof(TRequest).Name,
                    result.Category.ToString(),
                    result.MatchedPattern ?? "unknown");

                if (_options.BlockOnInjection)
                {
                    _logger.LogWarning(
                        "[SECURITY DEFENSE] Blocking {RequestType} for UserId={UserId}: Prompt Injection ({Category}) detected: '{Match}'",
                        typeof(TRequest).Name, userId, result.Category, result.MatchedPattern);

                    throw new PromptInjectionException(
                        result.Category,
                        result.MatchedPattern,
                        $"Memory payload rejected: Prompt injection attempt detected ({result.Category}). {result.Description}");
                }
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
