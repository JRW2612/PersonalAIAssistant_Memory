using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    /// <summary>
    /// Command to purge all memories for a user (GDPR Article 17 / employee offboarding).
    /// Requires ComplianceAuditor or Admin role to execute.
    /// </summary>
    public record PurgeUserMemoriesCommand(
        string TargetUserId,
        string RequestedByUserId,
        string PurgeReason
    ) : IRequest<int>;
}
