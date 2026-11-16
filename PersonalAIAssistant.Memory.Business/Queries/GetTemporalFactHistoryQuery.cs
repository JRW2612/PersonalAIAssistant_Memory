using MediatR;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Business.Queries;

/// <summary>
/// Retrieves the assertion history for one subject-predicate pair owned by a user.
/// </summary>
public record GetTemporalFactHistoryQuery(
    string UserId,
    string Subject,
    string Predicate
) : IRequest<IReadOnlyList<TemporalFact>>;
