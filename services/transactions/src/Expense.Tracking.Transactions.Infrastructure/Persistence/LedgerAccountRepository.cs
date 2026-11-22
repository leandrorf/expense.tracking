using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.AccountingExport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence;

internal sealed class LedgerAccountRepository(TransactionsDbContext dbContext) : ILedgerAccountRepository
{
    public async Task<IReadOnlyList<LedgerAccount>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        await dbContext.LedgerAccounts
            .Where(l => l.OwnerUserId == ownerUserId)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(LedgerAccount account, CancellationToken cancellationToken) =>
        await dbContext.LedgerAccounts.AddAsync(account, cancellationToken);

    public void Remove(LedgerAccount account) => dbContext.LedgerAccounts.Remove(account);
}
