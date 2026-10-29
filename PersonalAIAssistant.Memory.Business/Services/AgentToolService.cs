using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.Safety;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Core.Models.Safety;

namespace PersonalAIAssistant.Memory.Business.Services;

/// <summary>
/// Implements Letta/MemGPT-style agent self-editing tools, allowing the LLM agent to
/// read, append, replace, and assert temporal facts during its reasoning loops,
/// alongside physical actuator and scientific laboratory tools subject to pre-execution safety interlocks.
/// </summary>
public class AgentToolService : IAgentToolService
{
    private readonly IMediator _mediator;
    private readonly IToolSchemaValidator _schemaValidator;
    private readonly IToolInterlockService? _interlockService;
    private readonly ILogger<AgentToolService> _logger;

    public AgentToolService(
        IMediator mediator,
        IToolSchemaValidator schemaValidator,
        ILogger<AgentToolService> logger)
        : this(mediator, schemaValidator, null, logger)
    {
    }

    public AgentToolService(
        IMediator mediator,
        IToolSchemaValidator schemaValidator,
        IToolInterlockService? interlockService,
        ILogger<AgentToolService> logger)
    {
        _mediator = mediator;
        _schemaValidator = schemaValidator;
        _interlockService = interlockService;
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
            ),
            new(
                Name: "centrifuge_spin",
                Description: "Set spin parameters and activate laboratory benchtop centrifuge. Enforces rotor rating safety limits and lid latch interlocks.",
                Parameters: new
                {
                    type = "object",
                    properties = new
                    {
                        rpm = new { type = "number", description = "Target rotational speed in RPM (max safe: 10,000)." },
                        duration_seconds = new { type = "number", description = "Spin duration in seconds." },
                        lid_closed = new { type = "boolean", description = "Whether the safety lid is securely closed." }
                    },
                    required = new[] { "rpm", "duration_seconds" }
                }
            ),
            new(
                Name: "arm_joint_move",
                Description: "Command robotic arm end-effector to 3D Cartesian target coordinates with contact force limit. Subject to keep-out spatial zones.",
                Parameters: new
                {
                    type = "object",
                    properties = new
                    {
                        x = new { type = "number", description = "X coordinate in meters." },
                        y = new { type = "number", description = "Y coordinate in meters." },
                        z = new { type = "number", description = "Z coordinate in meters." },
                        force_n = new { type = "number", description = "Maximum commanded contact force in Newtons (max safe: 100 N)." }
                    },
                    required = new[] { "x", "y", "z" }
                }
            ),
            new(
                Name: "liquid_stir_reagent",
                Description: "Stir or mix liquid reagent in active workstation. Prohibited while foam or froth stands above rim without settling.",
                Parameters: new
                {
                    type = "object",
                    properties = new
                    {
                        stir_speed = new { type = "number", description = "Stir speed setting (1 to 10)." },
                        duration_seconds = new { type = "number", description = "Stir duration in seconds." },
                        froth_settled = new { type = "boolean", description = "Whether froth has settled below rim." }
                    },
                    required = new[] { "stir_speed" }
                }
            ),
            new(
                Name: "task_abort",
                Description: "Deliberately halt execution, retract actuators clear of spills or obstacles, and file an explicit failure/incident report.",
                Parameters: new
                {
                    type = "object",
                    properties = new
                    {
                        reason = new { type = "string", description = "Reason for halting execution." },
                        diagnosis = new { type = "string", description = "Root cause diagnosis of the detected anomaly." }
                    },
                    required = new[] { "reason" }
                }
            ),
            new(
                Name: "task_complete",
                Description: "Mark physical workflow as concluded with final outcome status and calibration report.",
                Parameters: new
                {
                    type = "object",
                    properties = new
                    {
                        success = new { type = "boolean", description = "Whether the objective was achieved." },
                        report = new { type = "string", description = "Final post-condition assessment." }
                    },
                    required = new[] { "success" }
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
            var effectiveArgs = request.Arguments;

            // Pre-execution Safety Interlocks
            if (_interlockService != null)
            {
                var interlockResult = _interlockService.Evaluate(request.ToolName, effectiveArgs, InterlockMode.Interlocked);
                if (!interlockResult.Allowed)
                {
                    throw new InterlockViolationException(
                        request.ToolName,
                        interlockResult.Violations,
                        $"[SAFETY INTERLOCK TRIPPED] {interlockResult.RejectionReason}");
                }

                if (interlockResult.ModifiedArguments.HasValue)
                {
                    effectiveArgs = interlockResult.ModifiedArguments.Value;
                }
            }

            switch (request.ToolName.ToLowerInvariant())
            {
                case "temporal_fact_upsert":
                {
                    var args = effectiveArgs;
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
                    var args = effectiveArgs;
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
                    var args = effectiveArgs;
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
                    var args = effectiveArgs;
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
                    var args = effectiveArgs;
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

                case "centrifuge_spin":
                {
                    var rpm = effectiveArgs.TryGetProperty("rpm", out var r) ? r.GetDouble() : 0.0;
                    var duration = effectiveArgs.TryGetProperty("duration_seconds", out var d) ? d.GetDouble() : 30.0;
                    return new AgentToolExecutionResponse(
                        ToolName: request.ToolName,
                        Success: true,
                        Output: new { status = "spinning", rpm, duration_seconds = duration });
                }

                case "arm_joint_move":
                {
                    var x = effectiveArgs.GetProperty("x").GetDouble();
                    var y = effectiveArgs.GetProperty("y").GetDouble();
                    var z = effectiveArgs.GetProperty("z").GetDouble();
                    var force = effectiveArgs.TryGetProperty("force_n", out var f) ? f.GetDouble() : 20.0;
                    return new AgentToolExecutionResponse(
                        ToolName: request.ToolName,
                        Success: true,
                        Output: new { status = "moved", x, y, z, force_n = force });
                }

                case "liquid_stir_reagent":
                {
                    var speed = effectiveArgs.TryGetProperty("stir_speed", out var s) ? s.GetDouble() : 1.0;
                    return new AgentToolExecutionResponse(
                        ToolName: request.ToolName,
                        Success: true,
                        Output: new { status = "stirred", stir_speed = speed });
                }

                case "task_abort":
                {
                    var reason = effectiveArgs.GetProperty("reason").GetString()!;
                    var diagnosis = effectiveArgs.TryGetProperty("diagnosis", out var diag) ? diag.GetString() : null;
                    return new AgentToolExecutionResponse(
                        ToolName: request.ToolName,
                        Success: true,
                        Output: new { status = "aborted", deliberateHalt = true, reason, diagnosis });
                }

                case "task_complete":
                {
                    var success = effectiveArgs.GetProperty("success").GetBoolean();
                    var report = effectiveArgs.TryGetProperty("report", out var rep) ? rep.GetString() : null;
                    return new AgentToolExecutionResponse(
                        ToolName: request.ToolName,
                        Success: true,
                        Output: new { status = "completed", success, report });
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
