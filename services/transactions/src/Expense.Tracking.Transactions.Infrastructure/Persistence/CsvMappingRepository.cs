using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.Imports.Csv;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence;

internal sealed class CsvMappingRepository(TransactionsDbContext dbContext) : ICsvMappingRepository
{
    public async Task<IReadOnlyList<SavedCsvMapping>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        await dbContext.CsvMappings
            .Where(m => m.OwnerUserId == ownerUserId)
            .OrderBy(m => m.Name)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(SavedCsvMapping mapping, CancellationToken cancellationToken) =>
        await dbContext.CsvMappings.AddAsync(mapping, cancellationToken);

    public void Remove(SavedCsvMapping mapping) => dbContext.CsvMappings.Remove(mapping);
}
