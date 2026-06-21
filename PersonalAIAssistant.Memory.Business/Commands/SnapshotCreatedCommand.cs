using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public class SnapshotCreatedCommand : IRequest<Guid>
    {
        public Guid AggregateIdSnapshot { get; set; }
        public string SnapshotPayload { get; set; }   // JSON summary of state
        public int SnapshotVersion { get; set; }
    }
}
