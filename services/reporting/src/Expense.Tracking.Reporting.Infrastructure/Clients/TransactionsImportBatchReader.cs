using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Invoices;

namespace Expense.Tracking.Reporting.Infrastructure.Clients;

/// <summary>
/// RCN-4 (ADR-0011, decisao 1): le o lote de importacao em <c>GET /imports/{id}</c> do transactions, com o token
/// do usuario encaminhado (os servicos aceitam o mesmo token). O transactions filtra pelo dono: 404 = nao existe
/// ou nao e do usuario. Outras falhas sobem.
/// </summary>
public sealed class TransactionsImportBatchReader(HttpClient httpClient, IUserTokenAccessor tokenAccessor) : IImportBatchReader
{
    public async Task<ImportBatchSnapshot?> GetAsync(Guid batchId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"imports/{batchId}");
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
        return await response.Content.ReadFromJsonAsync<ImportBatchSnapshot>(cancellationToken)
            ?? throw new InvalidOperationException("O transactions respondeu a importacao vazia.");
    }
}
