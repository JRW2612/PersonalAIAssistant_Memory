namespace PersonalAIAssistant.Memory.Core.Interfaces.Others
{

    /// <summary>
    /// Optional capability for repositories that can execute a unit of work inside a DB transaction.
    /// Implement in EF-backed repository to get transactional batched projection.
    /// </summary>
    public interface ITransactionalReadModelRepository
    {
        Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct);
    }
}
