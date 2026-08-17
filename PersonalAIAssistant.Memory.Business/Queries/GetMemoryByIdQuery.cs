using MediatR;
using PersonalAIAssistant.Memory.Core.Models;
using System;

namespace PersonalAIAssistant.Memory.Business.Queries
{
    public record GetMemoryByIdQuery(Guid MemoryId, string UserId) : IRequest<MemoryReadModel?>;
}
