using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// RJT-2 (ADR-0010, decisoes 1, 2 e 9): valor com vigencia em <c>/transactions/recurrences/{id}/amounts</c>, e o
/// valor do periodo no lancamento pre-preenchido e no "a lancar".
/// </summary>
public sealed class RecurrenceAmountsApiTests(TransactionsApiFactory factory) : IClassFixture<TransactionsApiFactory>
{
    private readonly TransactionsApiFactory _factory = factory;

    private HttpClient ClientFor(Guid owner) => AuthenticatedAs(owner.ToString(), null);

    private HttpClient AuthenticatedAs(string sub, string? scope)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, sub);
        if (scope is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, scope);
        }

        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> TitleAsync(HttpResponseMessage response) => (await Json(response)).GetProperty("title").GetString();

    private async Task<(HttpClient Client, Guid AccountId, Guid RuleId)> RuleAsync(string startsOn = "2030-01-10")
    {
        var (client, accountId) = (ClientFor(Guid.NewGuid()), Guid.NewGuid());
        var created = await Json(await client.PostAsJsonAsync("/transactions/recurrences", RecurrencesApiTests.Rule(accountId, startsOn: startsOn)));
        return (client, accountId, created.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task NewAmount_AppliesFromItsPeriod_InTheLaunchAndInTheEvent()
    {
        var (client, _, ruleId) = await RuleAsync();

        var set = await client.PostAsJsonAsync($"/transactions/recurrences/{ruleId}/amounts", new { fromPeriod = "2030-03", amount = 2100m, version = 1 });

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var dto = await Json(set);
        Assert.Equal(2, dto.GetProperty("version").GetInt32());
        Assert.Equal([("2030-01", 2000m), ("2030-03", 2100m)], dto.GetProperty("amounts").EnumerateArray()
            .Select(a => (a.GetProperty("fromPeriod").GetString(), a.GetProperty("amount").GetDecimal())));

        // Lancar sem valor usa o do periodo.
        var february = await Json(await client.PostAsync($"/transactions/recurrences/{ruleId}/occurrences/2030-02/launch", null));
        var march = await Json(await client.PostAsync($"/transactions/recurrences/{ruleId}/occurrences/2030-03/launch", null));
        Assert.Equal((2000m, 2100m), (february.GetProperty("amount").GetDecimal(), march.GetProperty("amount").GetDecimal()));

        var next = (await client.GetFromJsonAsync<JsonElement>($"/transactions/recurrences/{ruleId}")).GetProperty("nextOccurrence");
        // Janeiro segue em aberto: a proxima e ele, com o valor da primeira vigencia.
        Assert.Equal(("2030-01", 2000m), (next.GetProperty("period").GetString(), next.GetProperty("amount").GetDecimal()));

        var service = AuthenticatedAs("expense-tracking-service", "events.read");
        var saved = (await service.GetFromJsonAsync<JsonElement>("/events?after=0&limit=500")).GetProperty("events").EnumerateArray()
            .Where(e => e.GetProperty("aggregateId").GetGuid() == ruleId && e.GetProperty("eventType").GetString() == "RecurrenceSaved")
            .Last();
        Assert.Equal(2, saved.GetProperty("payload").GetProperty("amounts").GetArrayLength());
    }

    [Fact]
    public async Task NewAmount_RefusesClosedOrInvalidPeriods_StaleVersions_AndRemovesOnlyFutureOnes()
    {
        var (client, _, ruleId) = await RuleAsync();
        await client.PostAsync($"/transactions/recurrences/{ruleId}/occurrences/2030-02/skip", null);

        var closed = await client.PostAsJsonAsync($"/transactions/recurrences/{ruleId}/amounts", new { fromPeriod = "2030-02", amount = 2100m, version = 1 });
        var first = await client.PostAsJsonAsync($"/transactions/recurrences/{ruleId}/amounts", new { fromPeriod = "2030-01", amount = 2100m, version = 1 });
        var noVersion = await client.PostAsJsonAsync($"/transactions/recurrences/{ruleId}/amounts", new { fromPeriod = "2030-05", amount = 2100m });
        var ok = await client.PostAsJsonAsync($"/transactions/recurrences/{ruleId}/amounts", new { fromPeriod = "2030-05", amount = 2100m, version = 1 });
        var stale = await client.PostAsJsonAsync($"/transactions/recurrences/{ruleId}/amounts", new { fromPeriod = "2030-07", amount = 2200m, version = 1 });

        Assert.Equal((HttpStatusCode.BadRequest, "recurrence.amount_period_invalid"), (closed.StatusCode, await TitleAsync(closed)));
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Equal((HttpStatusCode.BadRequest, "recurrence.version_required"), (noVersion.StatusCode, await TitleAsync(noVersion)));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal((HttpStatusCode.Conflict, "recurrence.concurrency_conflict"), (stale.StatusCode, await TitleAsync(stale)));

        // 2030-05 ainda nao comecou (hoje e antes de 2030): pode sair.
        var removed = await client.DeleteAsync($"/transactions/recurrences/{ruleId}/amounts/2030-05?version=2");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Single((await Json(removed)).GetProperty("amounts").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/transactions/recurrences/{ruleId}/amounts/2030-05?version=3")).StatusCode);
    }

    [Fact]
    public async Task Due_UsesTheAmountOfEachPeriod()
    {
        // Comeca ha 70 dias: as duas primeiras ocorrencias passadas tem valores diferentes.
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-70);
        var (client, _, ruleId) = await RuleAsync(start.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        var second = start.AddMonths(1).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync($"/transactions/recurrences/{ruleId}/amounts", new { fromPeriod = second, amount = 2500m, version = 1 })).StatusCode);

        var due = (await client.GetFromJsonAsync<JsonElement>("/transactions/recurrences/due")).EnumerateArray()
            .ToDictionary(d => d.GetProperty("period").GetString()!, d => d.GetProperty("amount").GetDecimal());

        Assert.Equal(2500m, due[second]);
        Assert.All(due.Where(d => string.CompareOrdinal(d.Key, second) < 0), d => Assert.Equal(2000m, d.Value));
    }
}
