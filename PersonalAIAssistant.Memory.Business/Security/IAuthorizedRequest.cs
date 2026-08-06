namespace PersonalAIAssistant.Memory.Business.Security
{
    public interface IAuthorizedRequest
    {
        Guid MemoryId { get; }
        string UserId { get; }
    }
}
