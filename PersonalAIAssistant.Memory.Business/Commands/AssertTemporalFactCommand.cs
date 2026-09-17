using MediatR;
using PersonalAIAssistant.Memory.Core.Interfaces.Common;

namespace PersonalAIAssistant.Memory.Business.Commands;

/// <summary>
/// Command to assert an entity relationship fact with automated temporal invalidation.
/// </summary>
public record AssertTemporalFactCommand(
    string Subject,
    string Predicate,
    string Object,
    string UserId,
    double Confidence = 1.0,
    string? Source = null,
    DateTime? ValidFromUtc = null,
    string? CorrelationId = null
) : IRequest<Guid>, ICorrelatedRequest;
