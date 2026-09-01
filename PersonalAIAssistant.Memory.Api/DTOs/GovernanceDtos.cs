namespace PersonalAIAssistant.Memory.Api.DTOs
{
    public record LegalHoldDto(string Reason);
    public record PurgeRequestDto(string Reason);
    public record PurgeResponseDto(string UserId, int PurgedCount, string Status);
}
