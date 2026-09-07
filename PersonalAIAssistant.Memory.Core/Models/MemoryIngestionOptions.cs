namespace PersonalAIAssistant.Memory.Core.Models;

/// <summary>Controls normalization, redaction and classification before long-term persistence.</summary>
public sealed class MemoryIngestionOptions
{
    public const string SectionName = "MemoryIngestion";
    public bool Enabled { get; set; } = true;
    public bool RedactSensitiveData { get; set; } = true;
    public int MaxTags { get; set; } = 20;
    public int MaxTagLength { get; set; } = 64;
}
