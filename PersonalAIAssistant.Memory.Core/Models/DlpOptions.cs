namespace PersonalAIAssistant.Memory.Core.Models
{
    public sealed class DlpOptions
    {
        public const string SectionName = "Dlp";
        public bool Enabled { get; set; } = true;
        public bool BlockOnViolation { get; set; } = true;
        public bool MaskSensitiveData { get; set; } = false;
        public IList<string> AllowedCategories { get; set; } = new List<string>();
    }
}
