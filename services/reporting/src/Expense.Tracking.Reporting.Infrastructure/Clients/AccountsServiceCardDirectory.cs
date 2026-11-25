using System.Net.Http.Json;
using Expense.Tracking.Reporting.Application.Invoices;
using Expense.Tracking.Reporting.Application.Reminders;

namespace Expense.Tracking.Reporting.Infrastructure.Clients;

/// <summary>
/// NTF-8 (ADR-0009, decisao 6): cartoes de um dono pelo endpoint interno do accounts
/// (<c>GET /internal/credit-cards?ownerUserId=</c>), com token de servico (<c>accounts.cards.read</c>).
/// So o job do resumo usa: a API continua lendo os cartoes com o token do usuario.
/// </summary>
internal sealed class AccountsServiceCardDirectory(HttpClient httpClient) : IServiceCardDirectory
{
    public async Task<IReadOnlyList<CardInfo>> ListForOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"internal/credit-cards?ownerUserId={ownerUserId}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CardInfo>>(cancellationToken) ?? [];
    }
}
