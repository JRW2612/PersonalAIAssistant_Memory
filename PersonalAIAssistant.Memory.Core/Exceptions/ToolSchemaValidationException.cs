namespace PersonalAIAssistant.Memory.Core.Exceptions;

public sealed class ToolSchemaValidationException : DomainException
{
    public ToolSchemaValidationException(string toolName, string reason)
        : base($"Tool '{toolName}' parameters do not conform to its strict schema: {reason}")
    {
    }
}
