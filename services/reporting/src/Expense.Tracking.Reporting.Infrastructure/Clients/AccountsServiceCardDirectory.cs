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
    public async Task<IReadOnlyList<ScopedCard>> ListForOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"internal/credit-cards?ownerUserId={ownerUserId}", cancellationToken);
        response.EnsureSuccessStatusCode();
        // MET-9: o escopo da conta do cartao (accountScope) vem junto, para a meta do mes no resumo por e-mail.
        var cards = await response.Content.ReadFromJsonAsync<List<ServiceCard>>(cancellationToken) ?? [];
        return cards
            .Select(c => new ScopedCard(
                new CardInfo(c.Id, c.AccountId, c.Label, c.LastFourDigits, c.CreditLimit, c.ClosingDay, c.DueDay, c.IsActive, c.Cycles),
                c.AccountScope))
            .ToList();
    }

    private sealed record ServiceCard(
        Guid Id, Guid AccountId, string? AccountScope, string Label, string LastFourDigits, decimal CreditLimit, int ClosingDay,
        int DueDay, bool IsActive, IReadOnlyList<CardCycleInfo>? Cycles = null);
}
