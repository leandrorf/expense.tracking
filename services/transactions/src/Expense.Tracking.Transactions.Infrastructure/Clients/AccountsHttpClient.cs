using System.Net.Http.Headers;
using Expense.Tracking.Transactions.Application.Abstractions;

namespace Expense.Tracking.Transactions.Infrastructure.Clients;

/// <summary>
/// Chama o servico 'accounts' encaminhando o token do usuario. Como o accounts filtra
/// pelo dono do token, um 200 significa "existe E pertence ao usuario".
/// </summary>
internal sealed class AccountsHttpClient(HttpClient httpClient, IUserTokenAccessor tokenAccessor) : IAccountsClient
{
    public Task<bool> AccountExistsAsync(Guid accountId, CancellationToken cancellationToken) =>
        ExistsAsync($"accounts/{accountId}", cancellationToken);

    public Task<bool> CreditCardExistsAsync(Guid creditCardId, CancellationToken cancellationToken) =>
        ExistsAsync($"credit-cards/{creditCardId}", cancellationToken);

    private async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        var token = tokenAccessor.GetAccessToken();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }
}
