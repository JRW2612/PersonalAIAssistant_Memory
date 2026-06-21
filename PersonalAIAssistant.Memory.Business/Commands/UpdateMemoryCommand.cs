using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public class UpdateMemoryCommand : IRequest<Guid>
    {
        public Guid MemoryId { get; set; }
        public Dictionary<string, string> UpdatedFields { get; set; } = new();
    }
}
