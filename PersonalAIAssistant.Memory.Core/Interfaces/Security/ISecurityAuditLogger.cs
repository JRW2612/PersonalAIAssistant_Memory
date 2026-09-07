namespace PersonalAIAssistant.Memory.Core.Interfaces.Security
{
    /// <summary>
    /// Security audit logging contract for access events and AI exploit / threat events.
    /// Follows DIP: Core/Business depend on this abstraction, implemented in Infrastructure.
    /// </summary>
    public interface ISecurityAuditLogger
    {
        void LogAccessGranted(string userId, string action, string resourceId);
        void LogAccessDenied(string userId, string action, string resourceId, string reason);
        void LogDataModification(string userId, string action, string memoryId);
        void LogPromptInjectionAttempt(string userId, string action, string category, string matchedPattern);
        void LogSuspiciousInput(string userId, string action, string reason);
        void LogSsrfAttempt(string userId, string action, string url, string reason);
        void LogRateLimitExceeded(string identity, string endpoint);
        void LogAiActivity(string userId, string promptHash, string provider, string model, string operation, string outcome, string? toolName = null);
        void LogProviderConnected(string userId, string provider, string scopes);
        void LogProviderRevoked(string userId, string provider, string reason);
        void LogProviderTokenRefreshed(string userId, string provider);
        void LogProviderAccessAttempt(string userId, string provider, string operation, string outcome, string? reason = null);
    }
}
