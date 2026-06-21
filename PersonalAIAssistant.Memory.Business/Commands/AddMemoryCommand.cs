using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public class AddMemoryCommand : IRequest<Guid>
    {
        public string RawText { get; set; }
        public string Source { get; set; }
        public List<string> Tags { get; set; } = new();
    }
}
