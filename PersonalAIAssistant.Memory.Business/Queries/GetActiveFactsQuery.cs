using MediatR;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Business.Queries;

/// <summary>
/// Query to retrieve current, active temporal facts for a user.
/// </summary>
public record GetActiveFactsQuery(
    string UserId,
    string? Subject = null
) : IRequest<IReadOnlyList<TemporalFact>>;
