using MediatR;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using System.Text.Json;

namespace PersonalAIAssistant.Memory.Business.Behaviors;

/// <summary>Applies registered strict JSON Schema contracts before a tool-backed command reaches a handler.</summary>
public sealed class StrictToolSchemaValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly IToolSchemaValidator _validator;

    public StrictToolSchemaValidationBehavior(IToolSchemaValidator validator) => _validator = validator;

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var toolName = request switch
        {
            AddMemoryCommand => "memory.add",
            UpdateMemoryCommand => "memory.update",
            ConsolidateMemoriesCommand => "memory.consolidate",
            _ => null
        };

        if (toolName is not null)
            _validator.Validate(toolName, JsonSerializer.SerializeToElement(request, SerializerOptions));

        return next(cancellationToken);
    }
}
