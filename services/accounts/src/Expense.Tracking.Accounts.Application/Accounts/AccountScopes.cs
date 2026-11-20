using Expense.Tracking.Accounts.Application.Abstractions;
using Expense.Tracking.Accounts.Domain.Common;

namespace Expense.Tracking.Accounts.Application.Accounts;

/// <summary>Escopo de uma conta, como o endpoint interno devolve.</summary>
public sealed record AccountScopeDto(Guid AccountId, string Scope);

/// <summary>
/// EVT-4: escopo PF/PJ de varias contas de uma vez, para servicos (o transactions completa os
/// eventos de lancamentos anteriores ao outbox). Ids desconhecidos ficam de fora da resposta.
/// </summary>
public sealed class GetAccountScopesHandler(IAccountScopeLookup lookup)
{
    public const int MaxIds = 100;

    public async Task<Result<IReadOnlyList<AccountScopeDto>>> HandleAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
    {
        var ids = accountIds.Distinct().ToArray();
        if (ids.Length is 0 or > MaxIds)
        {
            return Result.Failure<IReadOnlyList<AccountScopeDto>>(Error.Validation(
                "account_scopes.ids_invalid", $"Informe de 1 a {MaxIds} ids de conta em 'ids'."));
        }

        var scopes = await lookup.GetScopesAsync(ids, cancellationToken);
        return scopes.Select(s => new AccountScopeDto(s.Key, s.Value.ToString())).ToArray();
    }
}
