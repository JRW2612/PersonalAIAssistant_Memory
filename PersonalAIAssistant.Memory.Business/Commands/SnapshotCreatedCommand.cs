using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public class SnapshotCreatedCommand
    (
         Guid AggregateIdSnapshot,
         string SnapshotPayload,   // JSON summary of state
         int SnapshotVersion
    ) : IRequest<Guid>;
}
