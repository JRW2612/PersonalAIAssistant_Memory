namespace PersonalAIAssistant.Memory.Core.Models;

/// <summary>Controls conservative automatic fact formation from user-authored conversation turns.</summary>
public sealed class AutomaticMemoryOptions
{
    public const string SectionName = "AutomaticMemory";

    public bool Enabled { get; set; } = true;
    public int MaxTurnCharacters { get; set; } = 4000;
    public int MaxFactsPerTurn { get; set; } = 5;
}
