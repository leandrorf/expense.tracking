using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Domain.Reports;

namespace Expense.Tracking.Reporting.Infrastructure.Clients;

internal static class HttpRequestAuth
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static readonly JsonSerializerOptions Json = JsonOptions;

    public static void AddBearer(HttpRequestMessage request, IUserTokenAccessor tokenAccessor)
    {
        var token = tokenAccessor.GetAccessToken();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
}

internal sealed class TransactionsHttpClient(HttpClient httpClient, IUserTokenAccessor tokenAccessor) : ITransactionsClient
{
    private sealed record Dto(Guid AccountId, string Type, string PaymentMethod, string Category, decimal Amount, string Currency);

    public async Task<IReadOnlyList<TransactionRecord>> GetTransactionsAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var query = new List<string>();
        if (filter.AccountId is { } accountId) query.Add($"accountId={accountId}");
        if (filter.From is { } from) query.Add($"from={from:yyyy-MM-dd}");
        if (filter.To is { } to) query.Add($"to={to:yyyy-MM-dd}");
        var path = "transactions" + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty);

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        HttpRequestAuth.AddBearer(request, tokenAccessor);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var items = await response.Content.ReadFromJsonAsync<List<Dto>>(HttpRequestAuth.Json, cancellationToken) ?? [];
        return items
            .Select(d => new TransactionRecord(d.AccountId, d.Type, d.PaymentMethod, d.Category, d.Amount, d.Currency))
            .ToArray();
    }
}

internal sealed class AccountsHttpClient(HttpClient httpClient, IUserTokenAccessor tokenAccessor) : IAccountsClient
{
    private sealed record Dto(Guid Id, string Scope);

    public async Task<IReadOnlyDictionary<Guid, string>> GetAccountScopesAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "accounts");
        HttpRequestAuth.AddBearer(request, tokenAccessor);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var items = await response.Content.ReadFromJsonAsync<List<Dto>>(HttpRequestAuth.Json, cancellationToken) ?? [];
        return items.ToDictionary(a => a.Id, a => a.Scope);
    }
}
