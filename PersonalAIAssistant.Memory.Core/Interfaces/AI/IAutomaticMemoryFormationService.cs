namespace PersonalAIAssistant.Memory.Core.Interfaces.AI;

/// <summary>
/// Extracts and persists only directly supported facts from an individual user-authored turn.
/// </summary>
public interface IAutomaticMemoryFormationService
{
    Task FormMemoriesFromUserTurnAsync(string userId, string content, CancellationToken ct = default);
}
