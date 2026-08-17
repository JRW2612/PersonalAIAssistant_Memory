using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Core.Interfaces.AI
{
    public interface IAiMetricsLogger
    {
        void Record(AiCallMetrics metrics);
    }
}
