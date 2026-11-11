using PersonalAIAssistant.Memory.Core.Domains.Enums;

namespace PersonalAIAssistant.Memory.Api.DTOs;

/// <summary>Transcript and provider metadata returned by an authorized meeting-platform connector.</summary>
public sealed record ImportMeetingTranscriptDto(
    string MeetingId,
    string Transcript,
    string? TranscriptId = null,
    string? Title = null,
    DateTimeOffset? StartedAtUtc = null,
    MemoryImportance Importance = MemoryImportance.High);
