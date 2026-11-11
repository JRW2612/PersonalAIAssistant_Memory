using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PersonalAIAssistant.Memory.Api.Services;

/// <summary>Fetches transcript text from a meeting provider using a user-authorized access token.</summary>
public sealed class MeetingTranscriptProviderClient
{
    private readonly HttpClient _http;

    public MeetingTranscriptProviderClient(IHttpClientFactory clients) => _http = clients.CreateClient("meeting-platforms");

    public Task<string> FetchAsync(string platform, string meetingId, string accessToken, string? transcriptId, string? organizerId, CancellationToken ct) =>
        platform.ToLowerInvariant() switch
        {
            "teams" => FetchTeamsAsync(meetingId, accessToken, transcriptId, organizerId, ct),
            "zoom" => FetchZoomAsync(meetingId, accessToken, ct),
            "meet" => FetchMeetAsync(meetingId, accessToken, transcriptId, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(platform))
        };

    private async Task<string> FetchTeamsAsync(string meetingId, string token, string? transcriptId, string? organizerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(organizerId))
            throw new ArgumentException("organizerId is required for Teams transcript retrieval.");
        var prefix = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(organizerId)}/onlineMeetings/{Uri.EscapeDataString(meetingId)}/transcripts";
        if (string.IsNullOrWhiteSpace(transcriptId))
        {
            using var listing = await SendAsync(HttpMethod.Get, prefix, token, ct);
            using var document = await JsonDocument.ParseAsync(await listing.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!document.RootElement.TryGetProperty("value", out var transcripts) || transcripts.GetArrayLength() == 0)
                throw new InvalidOperationException("No Teams transcript is available for this meeting.");
            transcriptId = transcripts.EnumerateArray()
                .Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }
        if (string.IsNullOrWhiteSpace(transcriptId)) throw new InvalidOperationException("Teams did not return a transcript ID.");
        using var content = await SendAsync(HttpMethod.Get,
            $"{prefix}/{Uri.EscapeDataString(transcriptId)}/content?$format=text/vtt", token, ct);
        return await content.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> FetchZoomAsync(string meetingId, string token, CancellationToken ct)
    {
        using var metadata = await SendAsync(HttpMethod.Get,
            $"https://api.zoom.us/v2/meetings/{Uri.EscapeDataString(meetingId)}/transcript", token, ct);
        using var document = await JsonDocument.ParseAsync(await metadata.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!document.RootElement.TryGetProperty("download_url", out var urlValue) ||
            !Uri.TryCreate(urlValue.GetString(), UriKind.Absolute, out var downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps ||
            !(downloadUri.Host.Equals("zoom.us", StringComparison.OrdinalIgnoreCase) || downloadUri.Host.EndsWith(".zoom.us", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Zoom did not return a trusted transcript download URL.");

        using var download = await SendAsync(HttpMethod.Get, downloadUri.AbsoluteUri, token, ct);
        return await download.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> FetchMeetAsync(string conferenceRecordId, string token, string? transcriptId, CancellationToken ct)
    {
        string transcriptResource;
        if (string.IsNullOrWhiteSpace(transcriptId))
        {
            using var listing = await SendAsync(HttpMethod.Get,
                $"https://meet.googleapis.com/v2/conferenceRecords/{Uri.EscapeDataString(conferenceRecordId)}/transcripts", token, ct);
            using var document = await JsonDocument.ParseAsync(await listing.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!document.RootElement.TryGetProperty("transcripts", out var transcripts) || transcripts.GetArrayLength() == 0)
                throw new InvalidOperationException("No Google Meet transcript is available for this conference.");
            transcriptResource = transcripts[0].GetProperty("name").GetString() ?? throw new InvalidOperationException("Google Meet returned an invalid transcript resource.");
        }
        else
        {
            var expectedPrefix = $"conferenceRecords/{conferenceRecordId}/transcripts/";
            if (transcriptId.StartsWith(expectedPrefix, StringComparison.Ordinal))
                transcriptResource = transcriptId;
            else if (Regex.IsMatch(transcriptId, "^[A-Za-z0-9_-]{1,256}$"))
                transcriptResource = $"conferenceRecords/{conferenceRecordId}/transcripts/{transcriptId}";
            else
                throw new ArgumentException("transcriptId is invalid for Google Meet.");
        }

        var lines = new List<string>();
        string? pageToken = null;
        var pages = 0;
        do
        {
            var query = pageToken is null ? string.Empty : $"?pageToken={Uri.EscapeDataString(pageToken)}";
            using var entries = await SendAsync(HttpMethod.Get,
                $"https://meet.googleapis.com/v2/{transcriptResource}/entries{query}", token, ct);
            using var entryDocument = await JsonDocument.ParseAsync(await entries.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (entryDocument.RootElement.TryGetProperty("transcriptEntries", out var values))
            {
                foreach (var item in values.EnumerateArray())
                {
                    var start = item.TryGetProperty("startTime", out var startValue) ? startValue.GetString() : null;
                    var participant = item.TryGetProperty("participant", out var participantValue) ? participantValue.GetString() : null;
                    var text = item.TryGetProperty("text", out var textValue) ? textValue.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(text)) lines.Add($"[{start}] {participant}: {text}");
                }
            }
            pageToken = entryDocument.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        } while (!string.IsNullOrWhiteSpace(pageToken) && ++pages < 100);

        if (lines.Count == 0) throw new InvalidOperationException("The Google Meet transcript has no entries.");
        return string.Join(Environment.NewLine, lines);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new HttpRequestException("Meeting provider transcript request failed.", null, System.Net.HttpStatusCode.BadGateway);
        }
        return response;
    }
}
