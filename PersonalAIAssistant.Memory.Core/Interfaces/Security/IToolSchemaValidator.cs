using System.Text.Json;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Security;

/// <summary>Validates a tool parameter object against its registered strict JSON schema.</summary>
public interface IToolSchemaValidator
{
    void Validate(string toolName, JsonElement parameters);
}
