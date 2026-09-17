using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Core.Interfaces.AI;

/// <summary>
/// Service managing the registration and execution of agent self-editing tools (Letta/MemGPT-style).
/// </summary>
public interface IAgentToolService
{
    /// <summary>
    /// Returns the schema specifications for all memory tools exposed to the LLM agent.
    /// </summary>
    IReadOnlyList<AgentToolDefinition> GetToolDefinitions();

    /// <summary>
    /// Executes a tool call invoked by the LLM agent, validating arguments and routing to domain operations.
    /// </summary>
    Task<AgentToolExecutionResponse> ExecuteToolAsync(
        string userId,
        AgentToolExecutionRequest request,
        CancellationToken ct = default);
}
