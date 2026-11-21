using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence;

internal sealed class ImportBatchRepository(TransactionsDbContext dbContext) : IImportBatchRepository
{
    /// <summary>Lotes vencidos apagados por rodada da limpeza, para nao segurar o banco.</summary>
    private const int CleanupBatchSize = 50;

    public async Task AddAsync(ImportBatch batch, CancellationToken cancellationToken) =>
        await dbContext.ImportBatches.AddAsync(batch, cancellationToken);

    public Task<ImportBatch?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) =>
        dbContext.ImportBatches
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.Id == id && b.OwnerUserId == ownerUserId, cancellationToken);

    public async Task<int> DeleteExpiredAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        // As linhas saem pelo ON DELETE CASCADE da chave estrangeira, sem carrega-las.
        var expired = await dbContext.ImportBatches
            .Where(b => b.Status == ImportBatchStatus.Pending && b.ExpiresAtUtc <= nowUtc)
            .OrderBy(b => b.ExpiresAtUtc)
            .Take(CleanupBatchSize)
            .ToListAsync(cancellationToken);

        dbContext.ImportBatches.RemoveRange(expired);
        await dbContext.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }
}
