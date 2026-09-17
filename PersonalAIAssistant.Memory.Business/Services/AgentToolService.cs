using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Business.Services;

/// <summary>
/// Implements Letta/MemGPT-style agent self-editing tools, allowing the LLM agent to
/// read, append, replace, and assert temporal facts during its reasoning loops.
/// </summary>
public class AgentToolService : IAgentToolService
{
    private readonly IMediator _mediator;
    private readonly IToolSchemaValidator _schemaValidator;
    private readonly ILogger<AgentToolService> _logger;

    public AgentToolService(
        IMediator mediator,
        IToolSchemaValidator schemaValidator,
        ILogger<AgentToolService> logger)
    {
        _mediator = mediator;
        _schemaValidator = schemaValidator;
        _logger = logger;
    }

    public IReadOnlyList<AgentToolDefinition> GetToolDefinitions()
    {
        return new List<AgentToolDefinition>
        {
            new(
                Name: "temporal_fact_upsert",
                Description: "Assert or update an entity relationship fact (Subject-Predicate-Object). If an existing fact with the same subject and predicate exists with a different value, it is automatically marked as superseded and invalidated with a temporal valid_to timestamp.",
                Parameters: new
                {
                    type = "object",
                    properties = new
                    {
                        subject = new { type = "string", description = "The entity subject (e.g. 'Alice', 'Company')." },
                        predicate = new { type = "string", description = "The relationship or property name (e.g. 'works_at', 'lives_in', 'preferred_framework')." },
                        @object = new { type = "string", description = "The value or target entity (e.g. 'Google', 'Seattle', 'ASP.NET Core')." },
                        confidence = new { type = "number", description = "Confidence score between 0.0 and 1.0 (default 1.0)." }
                    },
                    required = new[] { "subject", "predicate", "object" }
                }
            ),
            new(
                Name: "core_memory_append",
                Description: "Append a message, preference, or active note directly into the working conversation memory session.",
                Parameters: new
                {
                    type = "object",
                    properties = new
                    {
                        role = new { type = "string", @enum = new[] { "user", "assistant", "system" }, description = "The speaker role." },
                        content = new { type = "string", description = "The message or note content." },
                        sessionId = new { type = "string", description = "The active conversation session ID." }
                    },
                    required = new[] { "role", "content", "sessionId" }
                }
            ),
            new(
                Name: "core_memory_replace",
                Description: "Update or replace the contents of an existing stored memory by its unique ID.",
                Parameters: new
                {
                    type = "object",
                    properties = new
                    {
                        memoryId = new { type = "string", format = "uuid", description = "Unique ID of the memory to replace." },
                        rawText = new { type = "string", description = "The new replacement text payload." }
                    },
                    required = new[] { "memoryId", "rawText" }
                }
            ),
            new(
                Name: "archival_memory_insert",
                Description: "Store a permanent fact, profile preference, or key summary into long-term archival memory.",
                Parameters: new
                {
                    type = "object",
                    properties = new
                    {
                        text = new { type = "string", description = "The content to permanently index." },
                        importance = new { type = "string", @enum = new[] { "Low", "Medium", "High", "Critical" }, description = "Importance level (default Medium)." },
                        tags = new { type = "array", items = new { type = "string" }, description = "Optional descriptive tags." }
                    },
                    required = new[] { "text" }
                }
            ),
            new(
                Name: "archival_memory_search",
                Description: "Search long-term archival memory for relevant facts, preferences, and history matching a semantic query.",
                Parameters: new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "Search query." },
                        topK = new { type = "integer", description = "Number of top results to retrieve (1 to 20)." }
                    },
                    required = new[] { "query" }
                }
            )
        };
    }

    public async Task<AgentToolExecutionResponse> ExecuteToolAsync(
        string userId,
        AgentToolExecutionRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Agent executing tool '{ToolName}' for user {UserId}", request.ToolName, userId);

        try
        {
            switch (request.ToolName.ToLowerInvariant())
            {
                case "temporal_fact_upsert":
                {
                    var args = request.Arguments;
                    var subject = args.GetProperty("subject").GetString()!;
                    var predicate = args.GetProperty("predicate").GetString()!;
                    var objectValue = args.GetProperty("object").GetString()!;
                    var confidence = args.TryGetProperty("confidence", out var conf) ? conf.GetDouble() : 1.0;

                    var command = new AssertTemporalFactCommand(
                        Subject: subject,
                        Predicate: predicate,
                        Object: objectValue,
                        UserId: userId,
                        Confidence: confidence,
                        Source: "AgentSelfEditingTool");

                    var factId = await _mediator.Send(command, ct);
                    return new AgentToolExecutionResponse(
                        ToolName: request.ToolName,
                        Success: true,
                        Output: new
                        {
                            factId,
                            status = "asserted",
                            message = $"Temporal fact [{subject}] {predicate} [{objectValue}] successfully recorded."
                        });
                }

                case "core_memory_append":
                {
                    var args = request.Arguments;
                    var role = args.GetProperty("role").GetString()!;
                    var content = args.GetProperty("content").GetString()!;
                    var sessionId = args.TryGetProperty("sessionId", out var s) && !string.IsNullOrEmpty(s.GetString())
                        ? s.GetString()!
                        : request.SessionId ?? "default";

                    var command = new AppendConversationTurnCommand(
                        UserId: userId,
                        SessionId: sessionId,
                        Role: role,
                        Content: content);

                    await _mediator.Send(command, ct);
                    return new AgentToolExecutionResponse(
                        ToolName: request.ToolName,
                        Success: true,
                        Output: new { status = "appended", sessionId, role });
                }

                case "core_memory_replace":
                {
                    var args = request.Arguments;
                    var memoryId = Guid.Parse(args.GetProperty("memoryId").GetString()!);
                    var rawText = args.GetProperty("rawText").GetString()!;

                    var command = new UpdateMemoryCommand(
                        MemoryId: memoryId,
                        UserId: userId,
                        UpdatedFields: new Dictionary<string, string> { { "RawText", rawText } });

                    await _mediator.Send(command, ct);
                    return new AgentToolExecutionResponse(
                        ToolName: request.ToolName,
                        Success: true,
                        Output: new { status = "updated", memoryId });
                }

                case "archival_memory_insert":
                {
                    var args = request.Arguments;
                    var text = args.GetProperty("text").GetString()!;
                    var importanceStr = args.TryGetProperty("importance", out var imp) ? imp.GetString() : "Medium";
                    var importance = Enum.TryParse<MemoryImportance>(importanceStr, true, out var parsed) ? parsed : MemoryImportance.Medium;

                    var tags = new List<string> { "agent-self-edited" };
                    if (args.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in t.EnumerateArray())
                        {
                            if (elem.GetString() is { } str) tags.Add(str);
                        }
                    }

                    var command = new AddMemoryCommand(
                        RawText: text,
                        Source: "AgentSelfEditingTool",
                        Tags: tags,
                        UserId: userId,
                        Importance: importance);

                    var memoryId = await _mediator.Send(command, ct);
                    return new AgentToolExecutionResponse(
                        ToolName: request.ToolName,
                        Success: true,
                        Output: new { status = "archived", memoryId });
                }

                case "archival_memory_search":
                {
                    var args = request.Arguments;
                    var query = args.GetProperty("query").GetString()!;
                    var topK = args.TryGetProperty("topK", out var k) ? Math.Clamp(k.GetInt32(), 1, 20) : 5;

                    var queryObj = new RetrieveMemoriesQuery(
                        UserId: userId,
                        QueryText: query,
                        TopK: topK);

                    var result = await _mediator.Send(queryObj, ct);
                    return new AgentToolExecutionResponse(
                        ToolName: request.ToolName,
                        Success: true,
                        Output: new
                        {
                            count = result.Sources.Count,
                            memories = result.Sources.Select(m => new
                            {
                                m.MemoryId,
                                m.Text,
                                m.Importance,
                                m.Score,
                                m.CreatedAt
                            })
                        });
                }

                default:
                    return new AgentToolExecutionResponse(
                        ToolName: request.ToolName,
                        Success: false,
                        Output: null,
                        Error: $"Unknown tool '{request.ToolName}'.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing agent tool '{ToolName}' for user {UserId}", request.ToolName, userId);
            return new AgentToolExecutionResponse(
                ToolName: request.ToolName,
                Success: false,
                Output: null,
                Error: ex.Message);
        }
    }
}
