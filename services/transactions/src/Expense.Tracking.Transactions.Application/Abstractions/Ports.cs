using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Application.Abstractions;

/// <summary>Filtros de consulta de lancamentos (sempre escopados pelo dono).</summary>
public sealed record TransactionQuery(
    Guid OwnerUserId,
    Guid? AccountId = null,
    string? Category = null,
    DateOnly? From = null,
    DateOnly? To = null);

public interface ITransactionRepository
{
    Task<Transaction?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Transaction>> ListAsync(TransactionQuery query, CancellationToken cancellationToken);

    Task AddAsync(Transaction transaction, CancellationToken cancellationToken);

    void Remove(Transaction transaction);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Cliente do servico 'accounts'. Verifica existencia/propriedade (o accounts filtra pelo
/// dono do token encaminhado), permitindo validar referencias sem acessar o banco dele.
/// </summary>
public interface IAccountsClient
{
    Task<bool> AccountExistsAsync(Guid accountId, CancellationToken cancellationToken);

    Task<bool> CreditCardExistsAsync(Guid creditCardId, CancellationToken cancellationToken);
}

/// <summary>Fornece o access token do usuario atual para encaminhar em chamadas entre servicos.</summary>
public interface IUserTokenAccessor
{
    string? GetAccessToken();
}
