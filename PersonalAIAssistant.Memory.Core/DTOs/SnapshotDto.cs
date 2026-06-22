namespace PersonalAIAssistant.Memory.Core.DTOs
{
    public record SnapshotDto(string StreamId, string Payload, int Version, DateTime CreatedAt);
}
