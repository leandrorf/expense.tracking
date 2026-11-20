using System.Net.Http.Json;
using Expense.Tracking.Transactions.Application.Abstractions;

namespace Expense.Tracking.Transactions.Infrastructure.Clients;

/// <summary>
/// EVT-4: le <c>GET /internal/account-scopes</c> do accounts com token de servico (escopo
/// <c>accounts.scopes.read</c>, posto pelo <see cref="Auth.ServiceTokenHandler"/>). Pede em
/// lotes do limite do endpoint.
/// </summary>
internal sealed class AccountScopeDirectoryHttpClient(HttpClient httpClient) : IAccountScopeDirectory
{
    /// <summary>Maximo de ids por chamada (o mesmo limite do endpoint do accounts).</summary>
    public const int BatchSize = 100;

    public async Task<IReadOnlyDictionary<Guid, string>> GetScopesAsync(
        IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
    {
        var scopes = new Dictionary<Guid, string>();
        foreach (var batch in accountIds.Distinct().Chunk(BatchSize))
        {
            var query = string.Join("&", batch.Select(id => $"ids={id}"));
            var items = await httpClient.GetFromJsonAsync<List<AccountScopeResponse>>(
                $"internal/account-scopes?{query}", cancellationToken) ?? [];
            foreach (var item in items)
            {
                scopes[item.AccountId] = item.Scope;
            }
        }

        return scopes;
    }

    private sealed record AccountScopeResponse(Guid AccountId, string Scope);
}
