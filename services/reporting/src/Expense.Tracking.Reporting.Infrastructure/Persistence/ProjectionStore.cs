using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Domain.Projection;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Reporting.Infrastructure.Persistence;

/// <summary>
/// Projecao e checkpoint no mesmo <see cref="ReportingDbContext"/>: o <c>SaveChanges</c> grava os
/// dois numa unica transacao de banco.
/// </summary>
internal sealed class ProjectionStore(ReportingDbContext dbContext) : IProjectionStore
{
    private ProjectionCheckpoint? _checkpoint;

    public async Task<long> GetCheckpointAsync(CancellationToken cancellationToken)
    {
        _checkpoint ??= await dbContext.Checkpoints.FindAsync([ProjectionCheckpoint.TransactionsFeed], cancellationToken);
        return _checkpoint?.LastSequence ?? 0;
    }

    // FindAsync tambem enxerga linhas adicionadas no lote atual e ainda nao gravadas.
    public async Task<ProjectedTransaction?> FindAsync(Guid transactionId, CancellationToken cancellationToken) =>
        await dbContext.Transactions.FindAsync([transactionId], cancellationToken);

    public void Add(ProjectedTransaction transaction) => dbContext.Transactions.Add(transaction);

    public void Remove(ProjectedTransaction transaction) => dbContext.Transactions.Remove(transaction);

    public async Task<ProjectedCardPayment?> FindPaymentAsync(Guid paymentId, CancellationToken cancellationToken) =>
        await dbContext.CardPayments.FindAsync([paymentId], cancellationToken);

    public void AddPayment(ProjectedCardPayment payment) => dbContext.CardPayments.Add(payment);

    public void RemovePayment(ProjectedCardPayment payment) => dbContext.CardPayments.Remove(payment);

    public async Task<ProjectedRecurrence?> FindRecurrenceAsync(Guid recurrenceId, CancellationToken cancellationToken) =>
        await dbContext.Recurrences.FindAsync([recurrenceId], cancellationToken);

    public void AddRecurrence(ProjectedRecurrence recurrence) => dbContext.Recurrences.Add(recurrence);

    public async Task RemoveRecurrenceAsync(ProjectedRecurrence recurrence, CancellationToken cancellationToken)
    {
        // Os ja gravados e os adicionados no lote atual (ainda so no contexto).
        var saved = await dbContext.RecurrenceOccurrences.Where(o => o.RecurrenceId == recurrence.RecurrenceId).ToListAsync(cancellationToken);
        var pending = dbContext.RecurrenceOccurrences.Local.Where(o => o.RecurrenceId == recurrence.RecurrenceId);
        dbContext.RecurrenceOccurrences.RemoveRange(saved.Union(pending).ToList());
        dbContext.Recurrences.Remove(recurrence);
    }

    public async Task<ProjectedRecurrenceOccurrence?> FindRecurrenceOccurrenceAsync(
        Guid recurrenceId, string period, CancellationToken cancellationToken) =>
        await dbContext.RecurrenceOccurrences.FindAsync([recurrenceId, period], cancellationToken);

    public void AddRecurrenceOccurrence(ProjectedRecurrenceOccurrence occurrence) => dbContext.RecurrenceOccurrences.Add(occurrence);

    public void RemoveRecurrenceOccurrence(ProjectedRecurrenceOccurrence occurrence) => dbContext.RecurrenceOccurrences.Remove(occurrence);

    public void SetCheckpoint(long sequence)
    {
        if (_checkpoint is null)
        {
            _checkpoint = new ProjectionCheckpoint { Feed = ProjectionCheckpoint.TransactionsFeed };
            dbContext.Checkpoints.Add(_checkpoint);
        }

        _checkpoint.LastSequence = sequence;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
