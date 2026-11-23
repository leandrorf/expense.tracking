using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// REC-3 (ADR-0008, decisoes 7 e 8): lancar, pular e desfazer o pulo de um periodo; "a lancar"; e a
/// exclusao do lancamento reabrindo o periodo.
/// </summary>
public sealed class RecurrenceOccurrencesApiTests(TransactionsApiFactory factory) : IClassFixture<TransactionsApiFactory>
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

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<(HttpClient Client, Guid AccountId, Guid RuleId)> RuleAsync(string startsOn = "2030-01-15", string? endsOn = null)
    {
        var (client, accountId) = (ClientFor(Guid.NewGuid()), Guid.NewGuid());
        var created = await client.PostAsJsonAsync("/transactions/recurrences",
            RecurrencesApiTests.Rule(accountId, startsOn: startsOn, endsOn: endsOn));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (client, accountId, (await Json(created)).GetProperty("id").GetGuid());
    }

    private async Task<List<JsonElement>> EventsForAsync(Guid aggregateId)
    {
        var service = AuthenticatedAs("expense-tracking-service", "events.read");
        var found = new List<JsonElement>();
        long after = 0;
        while (true)
        {
            var page = await service.GetFromJsonAsync<JsonElement>($"/events?after={after}&limit=500");
            var events = page.GetProperty("events").EnumerateArray().ToArray();
            if (events.Length == 0)
            {
                return found;
            }

            found.AddRange(events.Where(e => e.GetProperty("aggregateId").GetGuid() == aggregateId));
            after = events[^1].GetProperty("sequence").GetInt64();
        }
    }

    [Fact]
    public async Task Launch_CreatesALinkedTransaction_ClosesThePeriod_AndTheNextOccurrenceMovesOn()
    {
        var (client, accountId, ruleId) = await RuleAsync();

        var launched = await client.PostAsJsonAsync($"/transactions/recurrences/{ruleId}/occurrences/2030-01/launch",
            new { amount = 2075.5m });

        Assert.Equal(HttpStatusCode.Created, launched.StatusCode);
        var tx = await Json(launched);
        var txId = tx.GetProperty("id").GetGuid();
        Assert.Equal((accountId, "Aluguel", "Moradia", 2075.5m, "2030-01-15", "Contrato 2030", ruleId, "2030-01"), (
            tx.GetProperty("accountId").GetGuid(), tx.GetProperty("description").GetString(), tx.GetProperty("category").GetString(),
            tx.GetProperty("amount").GetDecimal(), tx.GetProperty("occurredOn").GetString(), tx.GetProperty("notes").GetString(),
            tx.GetProperty("recurrenceId").GetGuid(), tx.GetProperty("recurrencePeriod").GetString()));

        var rule = await client.GetFromJsonAsync<JsonElement>($"/transactions/recurrences/{ruleId}");
        Assert.Equal("2030-02", rule.GetProperty("nextOccurrence").GetProperty("period").GetString());

        // Um lancamento comum, com o TransactionRegistered de sempre; o periodo fecha no evento da regra.
        Assert.Equal("TransactionRegistered", Assert.Single(await EventsForAsync(txId)).GetProperty("eventType").GetString());
        var closed = (await EventsForAsync(ruleId)).Last();
        Assert.Equal(("RecurrenceOccurrenceClosed", "2030-01", "Launched", txId), (closed.GetProperty("eventType").GetString(),
            closed.GetProperty("payload").GetProperty("period").GetString(), closed.GetProperty("payload").GetProperty("status").GetString(),
            closed.GetProperty("payload").GetProperty("transactionId").GetGuid()));
    }

    [Fact]
    public async Task Launch_RefusesClosedPeriods_PeriodsOutsideTheSchedule_AndOtherUsersRules()
    {
        var (client, _, ruleId) = await RuleAsync(endsOn: "2030-06-30");
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsync($"/transactions/recurrences/{ruleId}/occurrences/2030-03/launch", null)).StatusCode);

        var again = await client.PostAsync($"/transactions/recurrences/{ruleId}/occurrences/2030-03/launch", null);
        var before = await client.PostAsync($"/transactions/recurrences/{ruleId}/occurrences/2029-12/launch", null);
        var after = await client.PostAsync($"/transactions/recurrences/{ruleId}/occurrences/2030-07/launch", null);
        var garbage = await client.PostAsync($"/transactions/recurrences/{ruleId}/occurrences/marco/launch", null);
        var other = await ClientFor(Guid.NewGuid()).PostAsync($"/transactions/recurrences/{ruleId}/occurrences/2030-04/launch", null);

        Assert.Equal((HttpStatusCode.Conflict, "recurrence.occurrence_closed"), (again.StatusCode, await TitleAsync(again)));
        Assert.All(new[] { before, after, garbage }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal("recurrence.period_invalid", await TitleAsync(before));
        Assert.Equal((HttpStatusCode.NotFound, "recurrence.not_found"), (other.StatusCode, await TitleAsync(other)));
    }

    [Fact]
    public async Task DeletingTheLaunchedTransaction_ReopensThePeriod()
    {
        var (client, _, ruleId) = await RuleAsync();
        var txId = (await Json(await client.PostAsync($"/transactions/recurrences/{ruleId}/occurrences/2030-01/launch", null)))
            .GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/transactions/{txId}")).StatusCode);

        var rule = await client.GetFromJsonAsync<JsonElement>($"/transactions/recurrences/{ruleId}");
        Assert.Equal("2030-01", rule.GetProperty("nextOccurrence").GetProperty("period").GetString());
        var reopened = (await EventsForAsync(ruleId)).Last();
        Assert.Equal(("RecurrenceOccurrenceReopened", "2030-01"), (reopened.GetProperty("eventType").GetString(),
            reopened.GetProperty("payload").GetProperty("period").GetString()));
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsync($"/transactions/recurrences/{ruleId}/occurrences/2030-01/launch", null)).StatusCode);
    }

    [Fact]
    public async Task Skip_ClosesThePeriod_UndoReopensIt_AndALaunchedPeriodIsNotUnskipped()
    {
        var (client, _, ruleId) = await RuleAsync();
        await client.PostAsync($"/transactions/recurrences/{ruleId}/occurrences/2030-02/launch", null);

        var skipped = await client.PostAsync($"/transactions/recurrences/{ruleId}/occurrences/2030-01/skip", null);
        var skippedTwice = await client.PostAsync($"/transactions/recurrences/{ruleId}/occurrences/2030-01/skip", null);
        var launchSkipped = await client.PostAsync($"/transactions/recurrences/{ruleId}/occurrences/2030-01/launch", null);
        var next = (await client.GetFromJsonAsync<JsonElement>($"/transactions/recurrences/{ruleId}")).GetProperty("nextOccurrence");
        var unskipLaunched = await client.DeleteAsync($"/transactions/recurrences/{ruleId}/occurrences/2030-02/skip");
        var unskipOpen = await client.DeleteAsync($"/transactions/recurrences/{ruleId}/occurrences/2030-05/skip");
        var unskipped = await client.DeleteAsync($"/transactions/recurrences/{ruleId}/occurrences/2030-01/skip");

        Assert.Equal(HttpStatusCode.NoContent, skipped.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, skippedTwice.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, launchSkipped.StatusCode);
        Assert.Equal("2030-03", next.GetProperty("period").GetString());
        Assert.Equal((HttpStatusCode.Conflict, "recurrence.occurrence_launched"), (unskipLaunched.StatusCode, await TitleAsync(unskipLaunched)));
        Assert.Equal((HttpStatusCode.NotFound, "recurrence.occurrence_not_skipped"), (unskipOpen.StatusCode, await TitleAsync(unskipOpen)));
        Assert.Equal(HttpStatusCode.NoContent, unskipped.StatusCode);
        Assert.Equal("2030-01",
            (await client.GetFromJsonAsync<JsonElement>($"/transactions/recurrences/{ruleId}")).GetProperty("nextOccurrence").GetProperty("period").GetString());

        var events = (await EventsForAsync(ruleId)).Select(e => e.GetProperty("eventType").GetString()).ToList();
        Assert.Equal(["RecurrenceSaved", "RecurrenceOccurrenceClosed", "RecurrenceOccurrenceClosed", "RecurrenceOccurrenceReopened"], events);
        var skipEvent = (await EventsForAsync(ruleId))[2].GetProperty("payload");
        Assert.Equal(("Skipped", JsonValueKind.Null), (skipEvent.GetProperty("status").GetString(), skipEvent.GetProperty("transactionId").ValueKind));
    }

    [Fact]
    public async Task Due_ListsOpenPastOccurrencesOfTheLast60Days_OldestFirst()
    {
        // Comeca 45 dias atras: a primeira ocorrencia (o proprio inicio) ja passou e esta "a lancar".
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-45);
        var (client, _, ruleId) = await RuleAsync(startsOn: Iso(start));
        var startPeriod = start.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        var due = (await client.GetFromJsonAsync<JsonElement>("/transactions/recurrences/due")).EnumerateArray().ToList();

        Assert.NotEmpty(due);
        Assert.All(due, d => Assert.Equal(ruleId, d.GetProperty("recurrenceId").GetGuid()));
        Assert.Equal((startPeriod, Iso(start), "Aluguel", 2000m), (due[0].GetProperty("period").GetString(),
            due[0].GetProperty("scheduledOn").GetString(), due[0].GetProperty("description").GetString(), due[0].GetProperty("amount").GetDecimal()));
        var dates = due.Select(d => d.GetProperty("scheduledOn").GetString()).ToList();
        Assert.Equal(dates.Order(StringComparer.Ordinal), dates);

        // Pulada, sai da lista.
        await client.PostAsync($"/transactions/recurrences/{ruleId}/occurrences/{startPeriod}/skip", null);
        var afterSkip = (await client.GetFromJsonAsync<JsonElement>("/transactions/recurrences/due")).EnumerateArray();
        Assert.DoesNotContain(afterSkip, d => d.GetProperty("period").GetString() == startPeriod);
    }
}
