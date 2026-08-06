using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Core.Models;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PersonalAIAssistant.Memory.Infrastructure.AI.Teams
{
    /// <summary>
    /// INotificationSender implementation that posts Adaptive Cards to a
    /// Microsoft Teams channel via an Incoming Webhook connector URL.
    ///
    /// Setup (one-time, no Azure registration needed):
    ///   1. In Teams, open the channel → ··· → Connectors → Incoming Webhook → Configure.
    ///   2. Copy the generated URL into appsettings / user-secrets under Teams:WebhookUrl.
    /// </summary>
    public sealed class TeamsWebhookSender : INotificationSender
    {
        private readonly HttpClient _http;
        private readonly TeamsOptions _opts;
        private readonly ILogger<TeamsWebhookSender> _logger;

        public TeamsWebhookSender(
            IHttpClientFactory httpFactory,
            IOptions<TeamsOptions> opts,
            ILogger<TeamsWebhookSender> logger)
        {
            _http   = httpFactory.CreateClient("teams");
            _opts   = opts.Value;
            _logger = logger;
        }

        public async Task SendAsync(string title, string body, CancellationToken ct)
        {
            if (!_opts.Enabled)
            {
                _logger.LogDebug("[Teams] Notifications disabled — skipping send.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_opts.WebhookUrl))
            {
                _logger.LogWarning("[Teams] WebhookUrl is not configured — skipping notification.");
                return;
            }

            // Adaptive Card payload (version 1.4 — supported in all Teams clients)
            var card = new AdaptiveCardPayload
            {
                Attachments =
                [
                    new Attachment
                    {
                        Content = new AdaptiveCard
                        {
                            Body =
                            [
                                new TextBlock { Text = title, Size = "Large", Weight = "Bolder" },
                                new TextBlock { Text = body, Wrap = true }
                            ]
                        }
                    }
                ]
            };

            _logger.LogDebug("[Teams] Posting notification — title: {Title}", title);

            var response = await _http.PostAsJsonAsync(_opts.WebhookUrl, card, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("[Teams] Webhook POST failed — status: {Status}, body: {Body}",
                    response.StatusCode, error);
                response.EnsureSuccessStatusCode(); // surface to caller / Polly
            }

            _logger.LogInformation("[Teams] Notification sent — title: {Title}", title);
        }

        // ── Adaptive Card DTOs ───────────────────────────────────────────────────

        private sealed class AdaptiveCardPayload
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = "message";

            [JsonPropertyName("attachments")]
            public List<Attachment> Attachments { get; set; } = [];
        }

        private sealed class Attachment
        {
            [JsonPropertyName("contentType")]
            public string ContentType { get; set; } = "application/vnd.microsoft.card.adaptive";

            [JsonPropertyName("content")]
            public AdaptiveCard Content { get; set; } = new();
        }

        private sealed class AdaptiveCard
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = "AdaptiveCard";

            [JsonPropertyName("version")]
            public string Version { get; set; } = "1.4";

            [JsonPropertyName("body")]
            public List<object> Body { get; set; } = [];
        }

        private sealed class TextBlock
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = "TextBlock";

            [JsonPropertyName("text")]
            public string Text { get; set; } = "";

            [JsonPropertyName("size")]
            public string? Size { get; set; }

            [JsonPropertyName("weight")]
            public string? Weight { get; set; }

            [JsonPropertyName("wrap")]
            public bool? Wrap { get; set; }
        }
    }
}
