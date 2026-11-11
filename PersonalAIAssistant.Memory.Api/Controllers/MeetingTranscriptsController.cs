using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PersonalAIAssistant.Memory.Api.DTOs;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Api.Services;

namespace PersonalAIAssistant.Memory.Api.Controllers;

/// <summary>Receives transcripts fetched by a user's authorized Teams, Zoom, or Meet connector.</summary>
[Authorize]
[ApiController]
[Route("api/v1/meetings")]
public sealed class MeetingTranscriptsController : ControllerBase
{
    private static readonly IReadOnlyDictionary<string, (string Scope, string Source)> Platforms =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["teams"] = ("OnlineMeetingTranscript.Read.All", "Teams"),
            ["zoom"] = ("cloud_recording:read:meeting_transcript", "Zoom"),
            ["meet"] = ("https://www.googleapis.com/auth/meetings.space.readonly", "GoogleMeet")
        };

    private readonly IMediator _mediator;
    private readonly IUserContext _userContext;
    private readonly IProviderTokenService _tokens;
    private readonly MeetingTranscriptProviderClient _connector;

    public MeetingTranscriptsController(IMediator mediator, IUserContext userContext, IProviderTokenService tokens, MeetingTranscriptProviderClient connector)
    {
        _mediator = mediator;
        _userContext = userContext;
        _tokens = tokens;
        _connector = connector;
    }

    /// <summary>Stores one transcript after a connected platform connector has fetched it from the provider.</summary>
    [HttpPost("{platform}/transcripts")]
    [EnableRateLimiting("MemoryWritePolicy")]
    [ProducesResponseType(typeof(CreateMemoryResponseDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> ImportTranscript(string platform, [FromBody] ImportMeetingTranscriptDto request, CancellationToken ct)
    {
        if (!Platforms.TryGetValue(platform, out var configuration))
            return BadRequest(new { error = "unsupported_meeting_platform", supported = Platforms.Keys });
        if (string.IsNullOrWhiteSpace(request.Transcript) || request.Transcript.Length > 1_000_000)
            return BadRequest(new { error = "transcript_required_or_too_large", maxCharacters = 1_000_000 });
        if (string.IsNullOrWhiteSpace(request.MeetingId) || request.MeetingId.Length > 256)
            return BadRequest(new { error = "meeting_id_required", maxLength = 256 });
        if (!await HasProviderAccessAsync(platform, configuration.Scope, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "provider_connection_missing_or_scope_not_consented", provider = platform, requiredScope = configuration.Scope });

        return await StoreTranscriptAsync(platform, request.MeetingId, request.Transcript, request.TranscriptId,
            request.Title, request.StartedAtUtc, request.Importance, configuration.Source, ct);
    }

    /// <summary>Fetches a completed transcript from a connected provider and stores it as memory.</summary>
    [HttpPost("{platform}/{meetingId}/import")]
    [EnableRateLimiting("MemoryWritePolicy")]
    [ProducesResponseType(typeof(CreateMemoryResponseDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> FetchAndImport(string platform, string meetingId,
        [FromQuery] string? transcriptId, [FromQuery] string? organizerId, [FromQuery] string? title,
        [FromQuery] DateTimeOffset? startedAtUtc, CancellationToken ct)
    {
        if (!Platforms.TryGetValue(platform, out var configuration))
            return BadRequest(new { error = "unsupported_meeting_platform", supported = Platforms.Keys });
        if (string.IsNullOrWhiteSpace(meetingId) || meetingId.Length > 256)
            return BadRequest(new { error = "meeting_id_required", maxLength = 256 });
        if (transcriptId?.Length > 512 || organizerId?.Length > 256)
            return BadRequest(new { error = "meeting_identifier_too_long" });
        if (!await HasProviderAccessAsync(platform, configuration.Scope, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "provider_connection_missing_or_scope_not_consented", provider = platform, requiredScope = configuration.Scope });

        var token = await _tokens.GetValidAccessTokenAsync(_userContext.UserId, platform, configuration.Scope, ct);
        if (string.IsNullOrWhiteSpace(token))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "provider_connection_missing_or_expired", provider = platform });

        string transcript;
        try
        {
            transcript = await _connector.FetchAsync(platform, meetingId, token, transcriptId, organizerId, ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "meeting_provider_transcript_fetch_failed", provider = platform });
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { error = "meeting_transcript_unavailable", provider = platform });
        }

        if (transcript.Length > 1_000_000)
            return BadRequest(new { error = "transcript_too_large", maxCharacters = 1_000_000 });
        return await StoreTranscriptAsync(platform, meetingId, transcript, transcriptId, title, startedAtUtc,
            MemoryImportance.High, configuration.Source, ct);
    }

    private async Task<bool> HasProviderAccessAsync(string platform, string scope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_userContext.UserId)) return false;
        return await _tokens.HasScopeConsentAsync(_userContext.UserId, platform, scope, ct);
    }

    private async Task<IActionResult> StoreTranscriptAsync(string platform, string meetingId, string transcript,
        string? transcriptId, string? title, DateTimeOffset? startedAtUtc, MemoryImportance importance,
        string source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_userContext.UserId)) return Unauthorized();

        var meetingTitle = string.IsNullOrWhiteSpace(title) ? $"{source} meeting" : title.Trim();
        var content = $"Meeting: {meetingTitle}\nPlatform: {source}\nMeeting ID: {meetingId}\n" +
            (startedAtUtc is null ? string.Empty : $"Started (UTC): {startedAtUtc:O}\n") +
            $"\nTranscript:\n{transcript.Trim()}";
        var transcriptKey = string.IsNullOrWhiteSpace(transcriptId)
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(transcript)))
            : transcriptId;
        var memoryKey = SHA256.HashData(Encoding.UTF8.GetBytes($"{_userContext.UserId}:{platform}:{meetingId}:{transcriptKey}"));
        var deterministicMemoryId = new Guid(memoryKey.AsSpan(0, 16));
        var safeMeetingId = Convert.ToHexString(memoryKey).ToLowerInvariant();
        var memoryId = await _mediator.Send(new AddMemoryCommand(
            RawText: content,
            Source: source,
            Tags: new[] { "meeting-transcript", $"platform-{platform.ToLowerInvariant()}" },
            UserId: _userContext.UserId,
            Importance: importance,
            CorrelationId: $"meeting-{safeMeetingId}",
            DeterministicMemoryId: deterministicMemoryId), ct);

        return Created($"/api/v1/memories/{memoryId}", new CreateMemoryResponseDto(memoryId));
    }
}
