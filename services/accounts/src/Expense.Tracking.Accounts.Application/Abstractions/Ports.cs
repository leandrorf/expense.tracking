using Expense.Tracking.Accounts.Domain.Accounts;
using Expense.Tracking.Accounts.Domain.Cards;

namespace Expense.Tracking.Accounts.Application.Abstractions;

/// <summary>Repositorio de contas. Todas as consultas sao escopadas pelo dono (OwnerUserId).</summary>
public interface IAccountRepository
{
    Task<Account?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Account>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken);

    Task<bool> ExistsActiveAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken);

    Task AddAsync(Account account, CancellationToken cancellationToken);

    void Update(Account account);
}

/// <summary>Repositorio de cartoes de credito, tambem escopado pelo dono.</summary>
public interface ICreditCardRepository
{
    Task<CreditCard?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CreditCard>> ListAsync(Guid ownerUserId, Guid? accountId, CancellationToken cancellationToken);

    Task AddAsync(CreditCard card, CancellationToken cancellationToken);

    void Update(CreditCard card);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
