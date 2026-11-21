using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Expense.Tracking.Reporting.Application.Abstractions;
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
        using var request = new HttpRequestMessage(HttpMethod.Get, $"credit-cards/{cardId}");
        var token = tokenAccessor.GetAccessToken();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CardInfo>(cancellationToken)
            ?? throw new InvalidOperationException("O accounts respondeu o cartao vazio.");
    }
}
