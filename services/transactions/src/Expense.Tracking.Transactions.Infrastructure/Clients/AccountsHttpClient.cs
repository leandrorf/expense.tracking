using System.Net.Http.Headers;
using System.Net.Http.Json;
using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.Imports;

namespace Expense.Tracking.Transactions.Infrastructure.Clients;

/// <summary>
/// Chama o servico 'accounts' encaminhando o token do usuario. Como o accounts filtra
/// pelo dono do token, um 200 significa "existe E pertence ao usuario".
/// </summary>
internal sealed class AccountsHttpClient(HttpClient httpClient, IUserTokenAccessor tokenAccessor) : IAccountsClient
{
    public async Task<string?> FindAccountScopeAsync(Guid accountId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync($"accounts/{accountId}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var account = await response.Content.ReadFromJsonAsync<AccountResponse>(cancellationToken);
        return account?.Scope
            ?? throw new InvalidOperationException("O accounts respondeu a conta sem o campo 'scope'.");
    }

    public async Task<bool?> IsAccountActiveAsync(Guid accountId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync($"accounts/{accountId}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var account = await response.Content.ReadFromJsonAsync<AccountResponse>(cancellationToken);
        return account?.IsActive
            ?? throw new InvalidOperationException("O accounts respondeu a conta sem o campo 'isActive'.");
    }

    public async Task<IReadOnlyList<InvoiceCardHint>> ListActiveCardsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync($"credit-cards?accountId={accountId}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var cards = await response.Content.ReadFromJsonAsync<List<CardResponse>>(cancellationToken) ?? [];
        return cards.Where(c => c.IsActive).Select(c => new InvoiceCardHint(c.Id, c.DueDay)).ToList();
    }

    public Task<bool> CreditCardExistsAsync(Guid creditCardId, CancellationToken cancellationToken) =>
        ExistsAsync($"credit-cards/{creditCardId}", cancellationToken);

    private async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(path, cancellationToken);
        return response.IsSuccessStatusCode;
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

    /// <summary>So o que o transactions usa da conta devolvida pelo accounts.</summary>
    private sealed record AccountResponse(string? Scope, bool? IsActive);

    /// <summary>INV-11: so o que a sugestao de fatura usa do cartao.</summary>
    private sealed record CardResponse(Guid Id, int DueDay, bool IsActive);
}
