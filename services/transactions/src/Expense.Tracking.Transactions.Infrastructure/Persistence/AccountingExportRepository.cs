using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.AccountingExport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence;

internal sealed class AccountingExportRepository(TransactionsDbContext dbContext) : IAccountingExportRepository
{
    public Task<AccountingExportRecord?> FindAsync(
        Guid ownerUserId, DateOnly month, Guid? accountId, CancellationToken cancellationToken)
    {
        var account = accountId ?? AccountingExportRecord.AllAccounts;
        return dbContext.AccountingExports.FirstOrDefaultAsync(
            r => r.OwnerUserId == ownerUserId && r.Month == month && r.AccountId == account, cancellationToken);
    }

    public async Task AddAsync(AccountingExportRecord record, CancellationToken cancellationToken) =>
        await dbContext.AccountingExports.AddAsync(record, cancellationToken);
}
