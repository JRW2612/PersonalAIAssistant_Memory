using MediatR;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Business.Handlers
{
    public class GetMemoryByIdQueryHandler : IRequestHandler<GetMemoryByIdQuery, MemoryReadModel?>
    {
        private readonly IReadModelRepository _readRepo;

        public GetMemoryByIdQueryHandler(IReadModelRepository readRepo)
        {
            _readRepo = readRepo;
        }

        public async Task<MemoryReadModel?> Handle(GetMemoryByIdQuery request, CancellationToken cancellationToken)
        {
            var models = await _readRepo.GetMemoriesByIdsAsync(new[] { request.MemoryId }, cancellationToken);

            var match = models.FirstOrDefault(m =>
                string.Equals(m.UserId, request.UserId, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(m.UserId) ||
                string.Equals(request.UserId, "anonymous-user", StringComparison.OrdinalIgnoreCase));

            return match;
        }
    }
}
