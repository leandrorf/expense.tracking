using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.Recurrences;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence;

internal sealed class EconomicIndexRepository(TransactionsDbContext dbContext) : IEconomicIndexRepository
{
    public async Task<string?> LastMonthAsync(EconomicIndex index, CancellationToken cancellationToken) =>
        await dbContext.EconomicIndexes
            .Where(i => i.Index == index)
            .OrderByDescending(i => i.Month)
            .Select(i => i.Month)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<EconomicIndexValue>> ListAsync(
        EconomicIndex index, string fromMonth, string toMonth, CancellationToken cancellationToken) =>
        await dbContext.EconomicIndexes
            .Where(i => i.Index == index && string.Compare(i.Month, fromMonth) >= 0 && string.Compare(i.Month, toMonth) <= 0)
            .OrderBy(i => i.Month)
            .ToListAsync(cancellationToken);

    public void Add(EconomicIndexValue value) => dbContext.EconomicIndexes.Add(value);
}
