using Expense.Tracking.Accounts.Application.Abstractions;
using Expense.Tracking.Accounts.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Accounts.Infrastructure.Persistence.Repositories;

internal sealed class AccountRepository(AccountsDbContext dbContext) : IAccountRepository
{
    public Task<Account?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) =>
        dbContext.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.OwnerUserId == ownerUserId, cancellationToken);

    public async Task<IReadOnlyList<Account>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        await dbContext.Accounts
            .Where(a => a.OwnerUserId == ownerUserId)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsActiveAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) =>
        dbContext.Accounts.AnyAsync(a => a.Id == id && a.OwnerUserId == ownerUserId && a.IsActive, cancellationToken);

    public async Task AddAsync(Account account, CancellationToken cancellationToken) =>
        await dbContext.Accounts.AddAsync(account, cancellationToken);

    public void Update(Account account) => dbContext.Accounts.Update(account);
}
