namespace PersonalAIAssistant.Memory.Core.Models
{
    public sealed class RetentionOptions
    {
        public const string SectionName = "Retention";
        public int MaxMemoriesPerUser { get; set; } = 500;
        public int TtlDays { get; set; } = 365;
        public int ArchiveDays { get; set; } = 30;
        public double DecayLambda { get; set; } = 0.05;
        public bool HardDeleteEnabled { get; set; } = false;
    }
}
