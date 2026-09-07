using System.Text.Json;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;

namespace PersonalAIAssistant.Memory.Infrastructure.Security;

/// <summary>
/// Strict allow-list validator for the application's tool contracts. Unknown fields, missing
/// required fields, and values of the wrong JSON type are rejected before handlers execute.
/// </summary>
public sealed class StrictToolSchemaValidator : IToolSchemaValidator
{
    private sealed record Field(JsonValueKind Kind, bool Required = false, JsonValueKind? ArrayItemKind = null);

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, Field>> Schemas =
        new Dictionary<string, IReadOnlyDictionary<string, Field>>(StringComparer.Ordinal)
        {
            ["memory.add"] = new Dictionary<string, Field>(StringComparer.OrdinalIgnoreCase)
            {
                ["rawText"] = new(JsonValueKind.String, true),
                ["source"] = new(JsonValueKind.String, true),
                ["tags"] = new(JsonValueKind.Array, true, JsonValueKind.String),
                ["userId"] = new(JsonValueKind.String, true),
                ["importance"] = new(JsonValueKind.Number),
                ["correlationId"] = new(JsonValueKind.String)
            },
            ["memory.update"] = new Dictionary<string, Field>(StringComparer.OrdinalIgnoreCase)
            {
                ["memoryId"] = new(JsonValueKind.String, true),
                ["userId"] = new(JsonValueKind.String, true),
                ["updatedFields"] = new(JsonValueKind.Object),
                ["tenantId"] = new(JsonValueKind.String)
            },
            ["memory.consolidate"] = new Dictionary<string, Field>(StringComparer.OrdinalIgnoreCase)
            {
                ["newMemoryId"] = new(JsonValueKind.String, true),
                ["mergedMemoryIds"] = new(JsonValueKind.Array, true, JsonValueKind.String),
                ["consolidatedText"] = new(JsonValueKind.String, true),
                ["userId"] = new(JsonValueKind.String, true),
                ["provenanceLinks"] = new(JsonValueKind.Array, true, JsonValueKind.String)
            }
        };

    public void Validate(string toolName, JsonElement parameters)
    {
        if (!Schemas.TryGetValue(toolName, out var schema))
            throw new ToolSchemaValidationException(toolName, "no registered schema exists");
        if (parameters.ValueKind != JsonValueKind.Object)
            throw new ToolSchemaValidationException(toolName, "parameters must be a JSON object");

        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in parameters.EnumerateObject())
        {
            if (!schema.TryGetValue(property.Name, out var field))
                throw new ToolSchemaValidationException(toolName, $"unknown parameter '{property.Name}'");
            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                if (field.Required) throw new ToolSchemaValidationException(toolName, $"required parameter '{property.Name}' is null");
                continue;
            }
            if (property.Value.ValueKind != field.Kind)
                throw new ToolSchemaValidationException(toolName, $"parameter '{property.Name}' must be {field.Kind}");
            if (field.ArrayItemKind is { } itemKind && property.Value.EnumerateArray().Any(item => item.ValueKind != itemKind))
                throw new ToolSchemaValidationException(toolName, $"items in '{property.Name}' must be {itemKind}");
            present.Add(property.Name);
        }

        foreach (var (name, field) in schema.Where(entry => entry.Value.Required))
            if (!present.Contains(name)) throw new ToolSchemaValidationException(toolName, $"required parameter '{name}' is missing");
    }
}
