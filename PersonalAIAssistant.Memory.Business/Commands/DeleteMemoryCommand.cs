using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public class DeleteMemoryCommand : IRequest<bool>
    {
        public Guid MemoryId { get; set; }
        public string Reason { get; set; }   // e.g. "user request", "TTL expired"
    }
}
