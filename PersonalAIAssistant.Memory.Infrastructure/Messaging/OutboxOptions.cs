namespace PersonalAIAssistant.Memory.Infrastructure.Messaging
{
    public class OutboxOptions
    {
        /// <summary>
        /// How many days we keep dispatched outbox messages before cleaning them up.
        /// </summary>
        public int RetentionDays { get; set; } = 7;

        /// <summary>
        /// How often (in seconds) the cleanup job runs. Defaults to once per hour.
        /// </summary>
        public int CleanupIntervalSeconds { get; set; } = 3600; // 1 hour
    }
}
