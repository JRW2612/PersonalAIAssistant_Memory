namespace PersonalAIAssistant.Memory.Events
{
    /// <summary>Emitted when a compliance auditor places a legal hold on a memory.</summary>
    public sealed class LegalHoldAppliedEvent : MemoryEvent
    {
        public string Reason { get; set; } = string.Empty;
        public string AuditorId { get; set; } = string.Empty;
    }

    /// <summary>Emitted when a compliance auditor releases a legal hold from a memory.</summary>
    public sealed class LegalHoldReleasedEvent : MemoryEvent
    {
        public string AuditorId { get; set; } = string.Empty;
    }

    /// <summary>Emitted when all memories for a user are purged (GDPR/offboarding).</summary>
    public sealed class UserMemoriesPurgedEvent : MemoryEvent
    {
        public string PurgedByUserId { get; set; } = string.Empty;
        public string PurgeReason { get; set; } = string.Empty;
        public int MemoryCount { get; set; }
    }
}
