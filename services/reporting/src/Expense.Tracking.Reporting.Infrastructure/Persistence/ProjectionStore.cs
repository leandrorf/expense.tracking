using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Domain.Projection;

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
