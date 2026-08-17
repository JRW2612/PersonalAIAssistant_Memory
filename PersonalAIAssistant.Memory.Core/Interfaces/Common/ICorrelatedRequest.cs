namespace PersonalAIAssistant.Memory.Core.Interfaces.Common
{
    public interface ICorrelatedRequest
    {
        string? CorrelationId { get; }
    }
}
