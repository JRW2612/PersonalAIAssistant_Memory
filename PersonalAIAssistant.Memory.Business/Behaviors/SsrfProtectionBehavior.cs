using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Business.Behaviors;

/// <summary>
/// MediatR pipeline behavior that scans memory write payloads for Server-Side Request Forgery (SSRF) threats.
/// Defends against malicious attempts to plant cloud metadata (169.254.169.254) or private network URLs
/// into the memory database that could later be queried, processed, or fetched by AI pipelines.
/// </summary>
public sealed class SsrfProtectionBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IUrlSafetyValidator _urlValidator;
    private readonly AiThreatOptions _options;
    private readonly ISecurityAuditLogger _auditLogger;
    private readonly ILogger<SsrfProtectionBehavior<TRequest, TResponse>> _logger;

    public SsrfProtectionBehavior(
        IUrlSafetyValidator urlValidator,
        IOptions<AiThreatOptions> options,
        ISecurityAuditLogger auditLogger,
        ILogger<SsrfProtectionBehavior<TRequest, TResponse>> logger)
    {
        _urlValidator = urlValidator;
        _options = options.Value;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_options.EnableSsrfProtection)
        {
            return await next(cancellationToken);
        }

        var text = ExtractText(request);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var scan = _urlValidator.ScanText(text);
            if (scan.HasUnsafeUrl)
            {
                var userId = ExtractUserId(request);

                _auditLogger.LogSsrfAttempt(
                    userId,
                    typeof(TRequest).Name,
                    scan.MatchedUrl ?? "unknown",
                    scan.Reason);

                if (_options.BlockOnSsrf)
                {
                    _logger.LogWarning(
                        "[SECURITY DEFENSE] Blocking {RequestType} for UserId={UserId}: SSRF URL detected: '{Url}'. Reason: {Reason}",
                        typeof(TRequest).Name, userId, scan.MatchedUrl, scan.Reason);

                    throw new SsrfSecurityException(
                        scan.MatchedUrl,
                        $"Memory payload rejected: SSRF threat detected. {scan.Reason}");
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
