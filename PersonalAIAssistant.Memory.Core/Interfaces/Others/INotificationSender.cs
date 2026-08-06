namespace PersonalAIAssistant.Memory.Core.Interfaces.Others
{
    /// <summary>
    /// Sends structured notifications to an external channel (e.g., Microsoft Teams Incoming Webhook).
    /// </summary>
    public interface INotificationSender
    {
        /// <summary>
        /// Posts a notification with a title and body to the configured channel.
        /// </summary>
        /// <param name="title">Short headline for the notification card.</param>
        /// <param name="body">Markdown-formatted body text.</param>
        /// <param name="ct">Cancellation token.</param>
        Task SendAsync(string title, string body, CancellationToken ct);
    }
}
