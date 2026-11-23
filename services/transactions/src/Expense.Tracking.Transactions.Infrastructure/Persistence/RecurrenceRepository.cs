using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.Recurrences;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence;

internal sealed class RecurrenceRepository(TransactionsDbContext dbContext) : IRecurrenceRepository
{
    public Task<Recurrence?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) =>
        dbContext.Recurrences.FirstOrDefaultAsync(r => r.Id == id && r.OwnerUserId == ownerUserId, cancellationToken);

    public async Task<IReadOnlyList<Recurrence>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        await dbContext.Recurrences
            .Where(r => r.OwnerUserId == ownerUserId)
            .OrderBy(r => r.Description)
            .ThenBy(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Recurrence recurrence, CancellationToken cancellationToken) =>
        await dbContext.Recurrences.AddAsync(recurrence, cancellationToken);

    public async Task RemoveAsync(Recurrence recurrence, CancellationToken cancellationToken)
    {
        var closed = await dbContext.RecurrenceOccurrences.Where(o => o.RecurrenceId == recurrence.Id).ToListAsync(cancellationToken);
        dbContext.RecurrenceOccurrences.RemoveRange(closed);
        dbContext.Recurrences.Remove(recurrence);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlySet<string>>> ListClosedPeriodsAsync(
        Guid ownerUserId, IReadOnlyCollection<Guid> recurrenceIds, CancellationToken cancellationToken)
    {
        if (recurrenceIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlySet<string>>();
        }

        var rows = await dbContext.RecurrenceOccurrences.AsNoTracking()
            .Where(o => o.OwnerUserId == ownerUserId && recurrenceIds.Contains(o.RecurrenceId))
            .Select(o => new { o.RecurrenceId, o.Period })
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(r => r.RecurrenceId)
            .ToDictionary(g => g.Key, g => (IReadOnlySet<string>)g.Select(r => r.Period).ToHashSet(StringComparer.Ordinal));
    }

    public Task<RecurrenceOccurrence?> FindOccurrenceAsync(Guid recurrenceId, string period, CancellationToken cancellationToken) =>
        dbContext.RecurrenceOccurrences.FirstOrDefaultAsync(o => o.RecurrenceId == recurrenceId && o.Period == period, cancellationToken);

    public Task<RecurrenceOccurrence?> FindOccurrenceByTransactionAsync(Guid transactionId, CancellationToken cancellationToken) =>
        dbContext.RecurrenceOccurrences.FirstOrDefaultAsync(o => o.TransactionId == transactionId, cancellationToken);

    public void AddOccurrence(RecurrenceOccurrence occurrence) => dbContext.RecurrenceOccurrences.Add(occurrence);

    public void RemoveOccurrence(RecurrenceOccurrence occurrence) => dbContext.RecurrenceOccurrences.Remove(occurrence);
}
