using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Forecast;
using Expense.Tracking.Reporting.Application.Invoices;

namespace Expense.Tracking.Reporting.Infrastructure.Clients;

/// <summary>
/// INV-4 (ADR-0005, decisao 3): le o cartao no accounts com o token do usuario encaminhado. Como o
/// accounts filtra pelo dono do token, 404 significa "nao existe ou nao e do usuario". Outras falhas
/// sobem: sem o ciclo do cartao nao ha fatura.
/// </summary>
public sealed class AccountsCardDirectory(HttpClient httpClient, IUserTokenAccessor tokenAccessor) : ICardDirectory
{
    public async Task<CardInfo?> FindAsync(Guid cardId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync($"credit-cards/{cardId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CardInfo>(cancellationToken)
            ?? throw new InvalidOperationException("O accounts respondeu o cartao vazio.");
    }

    /// <summary>
    /// INV-8: cartoes e contas do usuario, juntados pelo id da conta (a lista de contas traz o escopo).
    /// As duas listas ja vem filtradas pelo dono do token.
    /// </summary>
    public async Task<IReadOnlyList<ScopedCard>> ListWithScopesAsync(CancellationToken cancellationToken)
    {
        var cardsTask = GetListAsync<CardInfo>("credit-cards", cancellationToken);
        var accountsTask = GetListAsync<AccountResponse>("accounts", cancellationToken);
        var scopes = (await accountsTask).ToDictionary(a => a.Id, a => a.Scope);
        return (await cardsTask).Select(c => new ScopedCard(c, scopes.GetValueOrDefault(c.AccountId))).ToList();
    }

    /// <summary>INV-13: contas do usuario com o saldo informado; a lista ja vem filtrada pelo dono do token.</summary>
    public Task<IReadOnlyList<AccountInfo>> ListAccountsAsync(CancellationToken cancellationToken) =>
        GetListAsync<AccountInfo>("accounts", cancellationToken);

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken) ?? [];
    }

    private async Task<HttpResponseMessage> SendAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        var token = tokenAccessor.GetAccessToken();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await httpClient.SendAsync(request, cancellationToken);
    }

    /// <summary>So o que a previsao usa da conta devolvida pelo accounts.</summary>
    private sealed record AccountResponse(Guid Id, string? Scope);
}
