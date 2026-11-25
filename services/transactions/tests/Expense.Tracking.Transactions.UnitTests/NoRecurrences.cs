using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.Recurrences;

namespace Expense.Tracking.Transactions.UnitTests;

/// <summary>Repositorio de recorrencias vazio, para os testes que nao tratam de recorrencia.</summary>
internal sealed class NoRecurrences : IRecurrenceRepository
{
    public Task<Recurrence?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) => Task.FromResult<Recurrence?>(null);

    public Task<IReadOnlyList<Recurrence>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Recurrence>>([]);

    public Task AddAsync(Recurrence recurrence, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RemoveAsync(Recurrence recurrence, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyDictionary<Guid, IReadOnlySet<string>>> ListClosedPeriodsAsync(
        Guid ownerUserId, IReadOnlyCollection<Guid> recurrenceIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlySet<string>>>(new Dictionary<Guid, IReadOnlySet<string>>());

    public Task<RecurrenceOccurrence?> FindOccurrenceAsync(Guid recurrenceId, string period, CancellationToken cancellationToken) =>
        Task.FromResult<RecurrenceOccurrence?>(null);

    public Task<RecurrenceOccurrence?> FindOccurrenceByTransactionAsync(Guid transactionId, CancellationToken cancellationToken) =>
        Task.FromResult<RecurrenceOccurrence?>(null);

    /// <summary>RJT-8: regras devolvidas por <see cref="ListByAdjustmentIndexAsync"/> (as do indice pedido).</summary>
    public List<Recurrence> Adjusted { get; } = [];

    public Task<IReadOnlyList<Recurrence>> ListByAdjustmentIndexAsync(EconomicIndex index, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Recurrence>>(Adjusted.Where(r => r.Adjustment?.Index == index).ToList());

    public void AddOccurrence(RecurrenceOccurrence occurrence)
    {
    }

    public void RemoveOccurrence(RecurrenceOccurrence occurrence)
    {
    }
}
