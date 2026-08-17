namespace PersonalAIAssistant.Memory.Core.Interfaces.Persistence
{
    public interface ITransactionalReadModelRepository
    {
        Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct);
    }
}
