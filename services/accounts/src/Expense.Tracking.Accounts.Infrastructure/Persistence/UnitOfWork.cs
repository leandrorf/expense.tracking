using Expense.Tracking.Accounts.Application.Abstractions;

namespace Expense.Tracking.Accounts.Infrastructure.Persistence;

internal sealed class UnitOfWork(AccountsDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
