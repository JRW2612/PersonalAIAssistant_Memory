using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PersonalAIAssistant.Memory.Infrastructure.AI.Teams
{
    /// <summary>
    /// INotificationSender implementation that posts Adaptive Cards to a
    /// Microsoft Teams channel via an Incoming Webhook connector URL.
    /// Defends against SSRF by validating the destination URL before making requests.
    /// </summary>
    public sealed class TeamsWebhookSender : INotificationSender
    {
        private readonly HttpClient _http;
        private readonly TeamsOptions _opts;
        private readonly AiThreatOptions _threatOptions;
        private readonly IUrlSafetyValidator _urlValidator;
        private readonly ILogger<TeamsWebhookSender> _logger;

        public TeamsWebhookSender(
            IHttpClientFactory httpFactory,
            IOptions<TeamsOptions> opts,
            IUrlSafetyValidator urlValidator,
            IOptions<AiThreatOptions> threatOptions,
            ILogger<TeamsWebhookSender> logger)
        {
            _http = httpFactory.CreateClient("teams");
            _opts = opts.Value;
            _urlValidator = urlValidator;
            _threatOptions = threatOptions.Value;
            _logger = logger;
        }

        public TeamsWebhookSender(
            IHttpClientFactory httpFactory,
            IOptions<TeamsOptions> opts,
            IUrlSafetyValidator urlValidator,
            ILogger<TeamsWebhookSender> logger)
            : this(httpFactory, opts, urlValidator, Microsoft.Extensions.Options.Options.Create(new AiThreatOptions()), logger)
        {
        }

        public async Task SendAsync(string title, string body, CancellationToken ct = default)
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

            // SSRF Validation: block internal network / metadata URLs and enforce allowlist
            var safetyResult = _urlValidator.ValidateUrl(
                _opts.WebhookUrl,
                allowedHosts: _threatOptions.AllowedWebhookHosts,
                requireHttps: _threatOptions.RequireHttps);

            if (!safetyResult.IsSafe)
            {
                _logger.LogError("[Teams][SSRF BLOCKED] Blocked unsafe webhook URL '{Url}': {Reason}",
                    _opts.WebhookUrl, safetyResult.Reason);
                throw new SsrfSecurityException(_opts.WebhookUrl, safetyResult.Reason);
            }

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
                response.EnsureSuccessStatusCode();
            }

            _logger.LogInformation("[Teams] Notification sent — title: {Title}", title);
        }

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
