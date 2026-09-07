namespace PersonalAIAssistant.Memory.Core.Interfaces.Security;

/// <summary>Sanitizes and classifies content before it can reach event or vector storage.</summary>
public interface IMemoryIngestionSanitizer
{
    SanitizedMemoryIngestion Sanitize(string text, IReadOnlyCollection<string>? tags);
}

public sealed record SanitizedMemoryIngestion(string Text, IReadOnlyList<string> Tags, IReadOnlyList<string> Categories);
