using System.Net.Http.Json;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Projection;

namespace Expense.Tracking.Reporting.Infrastructure.Clients;

/// <summary>
/// EVT-2: le <c>GET /events</c> do transactions. O token de servico (escopo <c>events.read</c>)
/// entra pelo <see cref="Auth.ServiceTokenHandler"/>; nao ha usuario nesta chamada.
/// </summary>
internal sealed class EventFeedHttpClient(HttpClient httpClient) : IEventFeedClient
{
    public async Task<FeedPage> ReadAsync(long afterSequence, int limit, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"events?after={afterSequence}&limit={limit}", cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<FeedPage>(HttpRequestAuth.Json, cancellationToken)
            ?? throw new InvalidOperationException("O feed de eventos respondeu vazio.");
    }
}
