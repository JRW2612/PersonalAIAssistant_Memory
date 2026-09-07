using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;

namespace PersonalAIAssistant.Memory.Infrastructure.Security
{
    /// <summary>
    /// Implements enterprise security audit logging for access events and AI threat events.
    /// Structured format compatible with SIEM ingestion (Splunk, Microsoft Sentinel, Datadog).
    /// </summary>
    public class SecurityAuditLogger : ISecurityAuditLogger
    {
        private readonly ILogger<SecurityAuditLogger> _logger;

        public SecurityAuditLogger(ILogger<SecurityAuditLogger> logger)
        {
            _logger = logger;
        }

        public void LogAccessGranted(string userId, string action, string resourceId)
        {
            _logger.LogInformation(
                "[SECURITY AUDIT] Action={Action} | Status=GRANTED | User={UserId} | Resource={ResourceId} | Timestamp={Timestamp}",
                action, userId, resourceId, DateTime.UtcNow);
        }

        public void LogAccessDenied(string userId, string action, string resourceId, string reason)
        {
            _logger.LogWarning(
                "[SECURITY AUDIT] Action={Action} | Status=DENIED | User={UserId} | Resource={ResourceId} | Reason={Reason} | Timestamp={Timestamp}",
                action, userId, resourceId, reason, DateTime.UtcNow);
        }

        public void LogDataModification(string userId, string action, string memoryId)
        {
            _logger.LogInformation(
                "[SECURITY AUDIT] Action={Action} | Status=SUCCESS | User={UserId} | MemoryId={MemoryId} | Timestamp={Timestamp}",
                action, userId, memoryId, DateTime.UtcNow);
        }

        public void LogPromptInjectionAttempt(string userId, string action, string category, string matchedPattern)
        {
            _logger.LogWarning(
                "[SECURITY AUDIT] Action={Action} | Status=BLOCKED | Threat=PROMPT_INJECTION | Category={Category} | Pattern='{Pattern}' | User={UserId} | Timestamp={Timestamp}",
                action, category, matchedPattern, userId, DateTime.UtcNow);
        }

        public void LogSuspiciousInput(string userId, string action, string reason)
        {
            _logger.LogWarning(
                "[SECURITY AUDIT] Action={Action} | Status=REJECTED | Threat=SUSPICIOUS_INPUT | Reason='{Reason}' | User={UserId} | Timestamp={Timestamp}",
                action, reason, userId, DateTime.UtcNow);
        }

        public void LogSsrfAttempt(string userId, string action, string url, string reason)
        {
            _logger.LogWarning(
                "[SECURITY AUDIT] Action={Action} | Status=BLOCKED | Threat=SSRF | Url='{Url}' | Reason='{Reason}' | User={UserId} | Timestamp={Timestamp}",
                action, url, reason, userId, DateTime.UtcNow);
        }

        public void LogRateLimitExceeded(string identity, string endpoint)
        {
            _logger.LogWarning(
                "[SECURITY AUDIT] Action=REQUEST | Status=RATE_LIMITED | Threat=POSSIBLE_BRUTE_FORCE | Identity={Identity} | Endpoint={Endpoint} | Timestamp={Timestamp}",
                identity, endpoint, DateTime.UtcNow);
        }

        public void LogAiActivity(string userId, string promptHash, string provider, string model, string operation, string outcome, string? toolName = null)
        {
            // Content and chain-of-thought are deliberately never emitted. The hash supports correlation
            // during an investigation without turning the audit stream into another sensitive datastore.
            _logger.LogInformation(
                "[AI AUDIT] UserId={UserId} PromptHash={PromptHash} Provider={Provider} Model={Model} Operation={Operation} Tool={Tool} Outcome={Outcome} Timestamp={Timestamp}",
                userId, promptHash, provider, model, operation, toolName, outcome, DateTime.UtcNow);
        }

        public void LogProviderConnected(string userId, string provider, string scopes)
        {
            _logger.LogInformation(
                "[PROVIDER AUDIT] Action=CONNECTED | User={UserId} | Provider={Provider} | Scopes='{Scopes}' | Timestamp={Timestamp}",
                userId, provider, scopes, DateTime.UtcNow);
        }

        public void LogProviderRevoked(string userId, string provider, string reason)
        {
            _logger.LogInformation(
                "[PROVIDER AUDIT] Action=REVOKED | User={UserId} | Provider={Provider} | Reason='{Reason}' | Timestamp={Timestamp}",
                userId, provider, reason, DateTime.UtcNow);
        }

        public void LogProviderTokenRefreshed(string userId, string provider)
        {
            _logger.LogInformation(
                "[PROVIDER AUDIT] Action=TOKEN_REFRESHED | User={UserId} | Provider={Provider} | Timestamp={Timestamp}",
                userId, provider, DateTime.UtcNow);
        }

        public void LogProviderAccessAttempt(string userId, string provider, string operation, string outcome, string? reason = null)
        {
            if (outcome.Equals("GRANTED", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "[PROVIDER AUDIT] Action=ACCESS_CHECK | Status=GRANTED | User={UserId} | Provider={Provider} | Operation={Operation} | Timestamp={Timestamp}",
                    userId, provider, operation, DateTime.UtcNow);
            }
            else
            {
                _logger.LogWarning(
                    "[PROVIDER AUDIT] Action=ACCESS_CHECK | Status=DENIED | User={UserId} | Provider={Provider} | Operation={Operation} | Reason='{Reason}' | Timestamp={Timestamp}",
                    userId, provider, operation, reason, DateTime.UtcNow);
            }
        }
    }
}
