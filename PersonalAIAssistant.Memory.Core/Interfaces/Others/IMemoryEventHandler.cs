using PersonalAIAssistant.Memory.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Others
{
    public interface IMemoryEventHandler
    {
        Task HandleAsync(MemoryEvent evt, CancellationToken ct);
    }
}
