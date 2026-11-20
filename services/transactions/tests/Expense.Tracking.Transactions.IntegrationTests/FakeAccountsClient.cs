using Expense.Tracking.Transactions.Application.Abstractions;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// Fake do IAccountsClient: por padrao toda conta existe com escopo <see cref="DefaultScope"/>;
/// <see cref="Scopes"/> define o escopo de contas especificas. Ids adicionados aos conjuntos
/// 'Invalid*' passam a nao existir (conta/cartao inexistente ou de outro dono).
/// </summary>
public sealed class FakeAccountsClient : IAccountsClient
{
    public const string DefaultScope = "Personal";

    public HashSet<Guid> InvalidAccounts { get; } = [];

    public HashSet<Guid> InvalidCards { get; } = [];

    public Dictionary<Guid, string> Scopes { get; } = [];

    public Task<string?> FindAccountScopeAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult(InvalidAccounts.Contains(accountId)
            ? null
            : Scopes.GetValueOrDefault(accountId, DefaultScope));

    public Task<bool> CreditCardExistsAsync(Guid creditCardId, CancellationToken cancellationToken) =>
        Task.FromResult(!InvalidCards.Contains(creditCardId));
}
