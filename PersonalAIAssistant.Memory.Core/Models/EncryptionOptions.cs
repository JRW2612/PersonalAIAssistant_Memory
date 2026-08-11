namespace PersonalAIAssistant.Memory.Core.Models
{
    public class EncryptionOptions
    {
        public const string SectionName = "Encryption";

        public bool Enabled { get; set; } = false;
        public string SystemKey { get; set; } = "default-fallback-system-secret-key-32bytes-long";
    }
}
