using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PersonalAIAssistant.Memory.Api.DTOs;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;

namespace PersonalAIAssistant.Memory.Api.Controllers;

/// <summary>Stateless MCP Streamable HTTP endpoint for Codex, Antigravity, and other agent clients.</summary>
[Authorize]
[ApiController]
[Route("mcp")]
public sealed class McpController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IUserContext _user;

    public McpController(IMediator mediator, IUserContext user)
    {
        _mediator = mediator;
        _user = user;
    }

    [HttpPost]
    [EnableRateLimiting("MemoryReadPolicy")]
    [RequestSizeLimit(1_100_000)]
    public async Task<IActionResult> Post([FromBody] JsonElement request, CancellationToken ct)
    {
        if (request.ValueKind != JsonValueKind.Object || !request.TryGetProperty("method", out var methodElement))
            return BadRequest(RpcError(null, -32600, "Invalid JSON-RPC request."));

        var method = methodElement.GetString();
        var id = request.TryGetProperty("id", out var requestId) ? requestId.Clone() : (JsonElement?)null;
        if (id is null) return Accepted(); // MCP notifications have no JSON-RPC response.

        try
        {
            return method switch
            {
                "initialize" => Ok(RpcResult(id.Value, new
                {
                    protocolVersion = "2025-03-26",
                    capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "personal-assistant-memory", version = "1.0.0" }
                })),
                "ping" => Ok(RpcResult(id.Value, new { })),
                "tools/list" => Ok(RpcResult(id.Value, new { tools = ToolDefinitions })),
                "tools/call" => await CallToolAsync(id.Value, request, ct),
                _ => Ok(RpcError(id, -32601, $"Method not found: {method}"))
            };
        }
        catch (JsonException)
        {
            return Ok(RpcError(id, -32602, "Invalid tool arguments."));
        }
        catch (Exception)
        {
            // Do not expose exception messages, provider responses, or stored content via MCP errors.
            return Ok(RpcError(id, -32603, "The memory operation failed."));
        }
    }

    private async Task<IActionResult> CallToolAsync(JsonElement id, JsonElement request, CancellationToken ct)
    {
        if (!request.TryGetProperty("params", out var parameters) ||
            !parameters.TryGetProperty("name", out var nameElement) ||
            !parameters.TryGetProperty("arguments", out var arguments) ||
            string.IsNullOrWhiteSpace(_user.UserId))
            return Ok(RpcError(id, -32602, "Tool name, arguments, and authenticated user are required."));

        var name = nameElement.GetString();
        if (name == "memory_search")
        {
            var query = arguments.GetProperty("query").GetString();
            if (string.IsNullOrWhiteSpace(query) || query.Length > 1000)
                return Ok(RpcError(id, -32602, "query must contain 1 to 1,000 characters."));
            var topK = arguments.TryGetProperty("topK", out var topKElement) && topKElement.TryGetInt32(out var parsedTopK)
                ? Math.Clamp(parsedTopK, 1, 20) : 5;
            var memories = await _mediator.Send(new RetrieveMemoriesQuery(_user.UserId, query, topK), ct);
            return Ok(ToolResult(id, memories));
        }

        if (name == "memory_save")
        {
            var content = arguments.GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content) || content.Length > 100_000)
                return Ok(RpcError(id, -32602, "content must contain 1 to 100,000 characters."));
            var source = arguments.TryGetProperty("source", out var sourceElement) ? sourceElement.GetString() : null;
            var memoryId = await _mediator.Send(new AddMemoryCommand(
                content,
                string.IsNullOrWhiteSpace(source) ? "Agent" : $"Agent:{source[..Math.Min(source.Length, 40)]}",
                new[] { "agent-memory" }, _user.UserId, MemoryImportance.Medium), ct);
            return Ok(ToolResult(id, new { memoryId }));
        }

        return Ok(RpcError(id, -32602, "Unknown tool."));
    }

    private static object ToolResult(JsonElement id, object value) => RpcResult(id, new
    {
        content = new[] { new { type = "text", text = JsonSerializer.Serialize(value) } },
        isError = false
    });

    private static object RpcResult(JsonElement id, object result) => new { jsonrpc = "2.0", id, result };
    private static object RpcError(JsonElement? id, int code, string message) => new
    {
        jsonrpc = "2.0", id, error = new { code, message }
    };

    private static readonly object[] ToolDefinitions =
    [
        new
        {
            name = "memory_search",
            description = "Search the authenticated user's saved memories for context relevant to a question or task.",
            inputSchema = new
            {
                type = "object",
                properties = new { query = new { type = "string", description = "Question or topic to find in memory." }, topK = new { type = "integer", description = "Maximum results (1-20)." } },
                required = new[] { "query" }
            }
        },
        new
        {
            name = "memory_save",
            description = "Save a decision, preference, or useful fact to the authenticated user's long-term memory.",
            inputSchema = new
            {
                type = "object",
                properties = new { content = new { type = "string", description = "Fact or decision to remember." }, source = new { type = "string", description = "Optional agent or application name." } },
                required = new[] { "content" }
            }
        }
    ];
}
