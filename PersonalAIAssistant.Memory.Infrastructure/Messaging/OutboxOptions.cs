namespace PersonalAIAssistant.Memory.Infrastructure.Messaging
{
    public class OutboxOptions
    {
        /// <summary>
        /// How many days to retain dispatched outbox messages before deletion.
        /// </summary>
        public int RetentionDays { get; set; } = 7;

        /// <summary>
        /// Cleanup interval in seconds between cleanup runs.
        /// </summary>
        public int CleanupIntervalSeconds { get; set; } = 3600; // 1 hour
    }
}
