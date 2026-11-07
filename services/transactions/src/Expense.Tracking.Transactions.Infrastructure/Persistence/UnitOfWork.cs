using Expense.Tracking.Transactions.Application.Abstractions;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence;

internal sealed class UnitOfWork(TransactionsDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
