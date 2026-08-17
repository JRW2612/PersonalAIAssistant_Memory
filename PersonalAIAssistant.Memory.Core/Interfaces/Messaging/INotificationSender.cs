namespace PersonalAIAssistant.Memory.Core.Interfaces.Messaging
{
    /// <summary>
    /// Outbound push notification abstraction (e.g. Teams, Slack, Email, Webhook).
    /// </summary>
    public interface INotificationSender
    {
        /// <summary>
        /// Sends a rich notification card/message to the configured notification channel.
        /// </summary>
        /// <param name="title">Notification headline / card title.</param>
        /// <param name="body">Notification markdown body / details.</param>
        /// <param name="ct">Cancellation token.</param>
        Task SendAsync(string title, string body, CancellationToken ct = default);
    }
}
