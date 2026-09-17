using System.Text.Json;

namespace PersonalAIAssistant.Memory.Core.Models;

/// <summary>
/// OpenAPI/JSON-Schema definition of an agent tool suitable for OpenAI or Gemini function calling.
/// </summary>
public record AgentToolDefinition(
    string Name,
    string Description,
    object Parameters);

/// <summary>
/// Request payload when an LLM agent executes a tool call.
/// </summary>
public record AgentToolExecutionRequest(
    string ToolName,
    JsonElement Arguments,
    string? SessionId = null);

/// <summary>
/// Standard response payload returned to the LLM agent after tool execution.
/// </summary>
public record AgentToolExecutionResponse(
    string ToolName,
    bool Success,
    object? Output,
    string? Error = null);
