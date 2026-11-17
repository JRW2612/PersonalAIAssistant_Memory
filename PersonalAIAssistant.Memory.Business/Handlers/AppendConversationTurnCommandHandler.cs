// PersonalAIAssistant.Memory.Business/Handlers/AppendConversationTurnCommandHandler.cs
using MediatR;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;

namespace PersonalAIAssistant.Memory.Business.Handlers;

public class AppendConversationTurnCommandHandler : IRequestHandler<AppendConversationTurnCommand, bool>
{
    private readonly IConversationBuffer _buffer;
    private readonly IAutomaticMemoryFormationService _memoryFormationService;
    private readonly ILogger<AppendConversationTurnCommandHandler> _logger;

    public AppendConversationTurnCommandHandler(
        IConversationBuffer buffer,
        IAutomaticMemoryFormationService memoryFormationService,
        ILogger<AppendConversationTurnCommandHandler> logger)
    {
        _buffer = buffer;
        _memoryFormationService = memoryFormationService;
        _logger = logger;
    }

    public async Task<bool> Handle(AppendConversationTurnCommand request, CancellationToken cancellationToken)
    {
        await _buffer.AppendTurnAsync(
            request.UserId,
            request.SessionId,
            request.Role.ToLowerInvariant(),
            request.Content,
            cancellationToken);

        if (string.Equals(request.Role, "user", StringComparison.OrdinalIgnoreCase))
        {
            await _memoryFormationService.FormMemoriesFromUserTurnAsync(
                request.UserId,
                request.Content,
                cancellationToken);
        }

        _logger.LogInformation("Successfully recorded {Role} turn for user {UserId}, session {SessionId}",
            request.Role, request.UserId, request.SessionId);

        return true;
    }
}
