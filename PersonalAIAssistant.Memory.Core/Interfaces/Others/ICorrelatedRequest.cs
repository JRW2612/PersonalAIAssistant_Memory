namespace PersonalAIAssistant.Memory.Core.Interfaces.Others
{
    /// <summary>
    /// Marker interface for MediatR requests (commands/queries) that carry a correlation ID
    /// for distributed tracing. Implement this on any command that has a CorrelationId property
    /// so the <c>LoggingBehavior</c> can propagate it instead of generating a fresh ID.
    /// </summary>
    public interface ICorrelatedRequest
    {
        string? CorrelationId { get; }
    }
}
