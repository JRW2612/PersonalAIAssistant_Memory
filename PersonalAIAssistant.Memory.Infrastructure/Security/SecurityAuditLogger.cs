using Microsoft.Extensions.Logging;
using System;

namespace PersonalAIAssistant.Memory.Infrastructure.Security
{
    public interface ISecurityAuditLogger
    {
        void LogAccessGranted(string userId, string action, string resourceId);
        void LogAccessDenied(string userId, string action, string resourceId, string reason);
        void LogDataModification(string userId, string action, string memoryId);
    }

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
    }
}
