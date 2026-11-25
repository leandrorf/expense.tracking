using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Recurrences;
using Expense.Tracking.Transactions.Domain.Recurrences;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// RJT-8 (ADR-0010, decisao 7): a taxa estimada vai no <c>RecurrenceSaved</c>; quando o job grava mes novo
/// do indice, as regras dele saem de novo no feed com a taxa nova.
/// </summary>
public sealed class IndexRepublishTests(TransactionsApiFactory factory) : IClassFixture<TransactionsApiFactory>
{
    private HttpClient AuthenticatedAs(string sub, string? scope = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, sub);
        if (scope is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, scope);
        }

        return client;
    }

    private async Task<JsonElement[]> SavedEventsOf(Guid ruleId) =>
        (await AuthenticatedAs("expense-tracking-service", "events.read").GetFromJsonAsync<JsonElement>("/events?after=0&limit=500"))
        .GetProperty("events").EnumerateArray()
        .Where(e => e.GetProperty("aggregateId").GetGuid() == ruleId && e.GetProperty("eventType").GetString() == "RecurrenceSaved")
        .ToArray();

    [Fact]
    public async Task NewIndexMonths_RepublishTheRule_WithTheEstimatedRate()
    {
        var accountId = Guid.NewGuid();
        var client = AuthenticatedAs(Guid.NewGuid().ToString());
        var rule = await (await client.PostAsJsonAsync("/transactions/recurrences", RecurrencesApiTests.Rule(accountId,
            startsOn: "2026-01-10", adjustment: new { anniversaryMonth = 3, kind = "Index", index = "IGPM" }))).Content.ReadFromJsonAsync<JsonElement>();
        var ruleId = rule.GetProperty("id").GetGuid();

        // Sem indice guardado, a taxa estimada vai nula.
        var created = Assert.Single(await SavedEventsOf(ruleId));
        Assert.Equal(JsonValueKind.Null, created.GetProperty("payload").GetProperty("adjustment").GetProperty("estimatedRate").ValueKind);

        // O job traz 12 meses do IGP-M de 0,5%: a regra sai de novo com 6,1678%.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = new DateOnly(today.Year, today.Month, 1).AddMonths(-12);
        var months = Enumerable.Range(0, 12)
            .Select(i => new IndexMonthRate(since.AddMonths(i).ToString("yyyy-MM", CultureInfo.InvariantCulture), 0.5m)).ToList();
        using (var scope = factory.Services.CreateScope())
        {
            var provider = scope.ServiceProvider;
            var job = new FetchEconomicIndexesJob(new IgpmSource(months), provider.GetRequiredService<IEconomicIndexRepository>(),
                provider.GetRequiredService<IRecurrenceRepository>(), new KnownScope(accountId), provider.GetRequiredService<IEventOutbox>(),
                provider.GetRequiredService<IUnitOfWork>(), new IndexFetchOptions(since),
                provider.GetRequiredService<LocalCalendar>(), TimeProvider.System);
            var result = await job.RunAsync(CancellationToken.None);
            Assert.Equal(1, result.Republished![EconomicIndex.IGPM]);
        }

        var republished = (await SavedEventsOf(ruleId)).Last().GetProperty("payload");
        Assert.Equal((6.1678m, "Personal", 1), (republished.GetProperty("adjustment").GetProperty("estimatedRate").GetDecimal(),
            republished.GetProperty("accountScope").GetString(), republished.GetProperty("recurrenceVersion").GetInt32()));

        // Editar a regra depois ja publica com a taxa estimada do que esta guardado.
        var edited = await client.PutAsJsonAsync($"/transactions/recurrences/{ruleId}", RecurrencesApiTests.Rule(accountId,
            startsOn: "2026-01-10", amount: 2100m, version: 1, adjustment: new { anniversaryMonth = 3, kind = "Index", index = "IGPM" }));
        edited.EnsureSuccessStatusCode();
        Assert.Equal(6.1678m, (await SavedEventsOf(ruleId)).Last().GetProperty("payload").GetProperty("adjustment").GetProperty("estimatedRate").GetDecimal());
    }

    private sealed class IgpmSource(IReadOnlyList<IndexMonthRate> months) : IEconomicIndexSource
    {
        public Task<IReadOnlyList<IndexMonthRate>> FetchAsync(EconomicIndex index, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult(index == EconomicIndex.IGPM ? months : (IReadOnlyList<IndexMonthRate>)[]);
    }

    private sealed class KnownScope(Guid accountId) : IAccountScopeDirectory
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetScopesAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string> { [accountId] = "Personal" });
    }
}
