using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.Imports;

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

    /// <summary>INV-6: contas que existem, mas foram desativadas.</summary>
    public HashSet<Guid> InactiveAccounts { get; } = [];

    public Task<string?> FindAccountScopeAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult(InvalidAccounts.Contains(accountId)
            ? null
            : Scopes.GetValueOrDefault(accountId, DefaultScope));

    public Task<bool> CreditCardExistsAsync(Guid creditCardId, CancellationToken cancellationToken) =>
        Task.FromResult(!InvalidCards.Contains(creditCardId));

    /// <summary>INV-11: cartoes ativos de cada conta (com o dia de vencimento), para a sugestao da importacao.</summary>
    public Dictionary<Guid, List<InvoiceCardHint>> AccountCards { get; } = [];

    public Task<IReadOnlyList<InvoiceCardHint>> ListActiveCardsAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InvoiceCardHint>>(AccountCards.GetValueOrDefault(accountId) ?? []);

    public Task<bool?> IsAccountActiveAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult<bool?>(InvalidAccounts.Contains(accountId) ? null : !InactiveAccounts.Contains(accountId));
}
