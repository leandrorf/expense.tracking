using Expense.Tracking.Identity.Application.Abstractions;

namespace Expense.Tracking.Identity.Infrastructure.Persistence;

internal sealed class UnitOfWork(IdentityDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
