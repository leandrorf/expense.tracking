using Expense.Tracking.Transactions.Application.Abstractions;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// Fake do IAccountsClient: por padrao tudo e valido; ids adicionados aos conjuntos
/// 'Invalid*' passam a retornar false (simulando conta/cartao inexistente ou de outro dono).
/// </summary>
public sealed class FakeAccountsClient : IAccountsClient
{
    public HashSet<Guid> InvalidAccounts { get; } = [];

    public HashSet<Guid> InvalidCards { get; } = [];

    public Task<bool> AccountExistsAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult(!InvalidAccounts.Contains(accountId));

    public Task<bool> CreditCardExistsAsync(Guid creditCardId, CancellationToken cancellationToken) =>
        Task.FromResult(!InvalidCards.Contains(creditCardId));
}
