using Expense.Tracking.Accounts.Application.Abstractions;
using Expense.Tracking.Accounts.Domain.Cards;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Accounts.Infrastructure.Persistence.Repositories;

internal sealed class CreditCardRepository(AccountsDbContext dbContext) : ICreditCardRepository
{
    public Task<CreditCard?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) =>
        dbContext.CreditCards.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, cancellationToken);

    public async Task<IReadOnlyList<CreditCard>> ListAsync(Guid ownerUserId, Guid? accountId, CancellationToken cancellationToken)
    {
        var query = dbContext.CreditCards.Where(c => c.OwnerUserId == ownerUserId);
        if (accountId is not null)
        {
            query = query.Where(c => c.AccountId == accountId);
        }

        return await query.OrderBy(c => c.Label).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(CreditCard card, CancellationToken cancellationToken) =>
        await dbContext.CreditCards.AddAsync(card, cancellationToken);

    public void Update(CreditCard card) => dbContext.CreditCards.Update(card);
}
