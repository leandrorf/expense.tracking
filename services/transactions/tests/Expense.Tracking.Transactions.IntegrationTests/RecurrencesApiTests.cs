using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// REC-2 (ADR-0008): regras de recorrencia em <c>/transactions/recurrences</c>, com o
/// <c>RecurrenceSaved</c>/<c>RecurrenceDeleted</c> no outbox no mesmo commit.
/// </summary>
public sealed class RecurrencesApiTests(TransactionsApiFactory factory) : IClassFixture<TransactionsApiFactory>
{
    private readonly TransactionsApiFactory _factory = factory;

    private HttpClient ClientFor(Guid owner, string? scope = null) => AuthenticatedAs(owner.ToString(), scope);

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

    internal static object Rule(Guid accountId, string startsOn = "2030-01-15", decimal amount = 2000m, string? endsOn = null,
        int? version = null, string method = "Boleto", Guid? cardId = null, string type = "Expense", string frequency = "Monthly",
        string description = "Aluguel", object? adjustment = null) => new
    {
        adjustment,
        accountId,
        type,
        paymentMethod = method,
        creditCardId = cardId,
        description,
        category = "Moradia",
        amount,
        currency = "BRL",
        notes = "Contrato 2030",
        frequency,
        startsOn,
        endsOn,
        version,
    };

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> TitleAsync(HttpResponseMessage response) => (await Json(response)).GetProperty("title").GetString();

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
    public async Task Create_SavesTheRule_ListsItWithTheNextOccurrence_AndPublishesRecurrenceSaved()
    {
        var (client, accountId) = (ClientFor(Guid.NewGuid()), Guid.NewGuid());
        _factory.AccountsClient.Scopes[accountId] = "Business";

        var created = await client.PostAsJsonAsync("/transactions/recurrences", Rule(accountId, startsOn: "2030-01-31"));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await Json(created);
        var id = dto.GetProperty("id").GetGuid();
        Assert.Equal(("Aluguel", 2000m, "Monthly", 1, "2030-01", "2030-01-31"), (dto.GetProperty("description").GetString(),
            dto.GetProperty("amount").GetDecimal(), dto.GetProperty("frequency").GetString(), dto.GetProperty("version").GetInt32(),
            dto.GetProperty("nextOccurrence").GetProperty("period").GetString(),
            dto.GetProperty("nextOccurrence").GetProperty("scheduledOn").GetString()));

        var listed = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/transactions/recurrences")).EnumerateArray());
        Assert.Equal(id, listed.GetProperty("id").GetGuid());

        var saved = Assert.Single(await EventsForAsync(id));
        Assert.Equal(("RecurrenceSaved", 2), (saved.GetProperty("eventType").GetString(), saved.GetProperty("version").GetInt32()));
        var payload = saved.GetProperty("payload");
        Assert.Equal((id, accountId, "Business", "Expense", "Boleto", "Monthly", "2030-01-31", 1), (
            payload.GetProperty("recurrenceId").GetGuid(), payload.GetProperty("accountId").GetGuid(),
            payload.GetProperty("accountScope").GetString(), payload.GetProperty("transactionType").GetString(),
            payload.GetProperty("paymentMethod").GetString(), payload.GetProperty("frequency").GetString(),
            payload.GetProperty("startsOn").GetString(), payload.GetProperty("recurrenceVersion").GetInt32()));
        Assert.False(payload.TryGetProperty("transactionId", out _));
        // RJT-2 (v2): as vigencias, a primeira no periodo do inicio.
        var amount = Assert.Single(payload.GetProperty("amounts").EnumerateArray());
        Assert.Equal(("2030-01", 2000m), (amount.GetProperty("fromPeriod").GetString(), amount.GetProperty("amount").GetDecimal()));
    }

    [Fact]
    public async Task Create_RefusesInvalidFields_UnknownAccountsAndCards()
    {
        var (client, accountId) = (ClientFor(Guid.NewGuid()), Guid.NewGuid());
        var (unknownAccount, unknownCard) = (Guid.NewGuid(), Guid.NewGuid());
        _factory.AccountsClient.InvalidAccounts.Add(unknownAccount);
        _factory.AccountsClient.InvalidCards.Add(unknownCard);

        var cases = new (object Body, HttpStatusCode Status, string Code)[]
        {
            (Rule(accountId, amount: 0m), HttpStatusCode.BadRequest, "money.amount_invalid"),
            (Rule(accountId, endsOn: "2029-12-31"), HttpStatusCode.BadRequest, "recurrence.ends_before_start"),
            (Rule(accountId, frequency: "Weekly"), HttpStatusCode.BadRequest, "recurrence.frequency_invalid"),
            (Rule(unknownAccount), HttpStatusCode.NotFound, "account.not_found"),
            (Rule(accountId, method: "CreditCard", cardId: unknownCard), HttpStatusCode.NotFound, "card.not_found"),
        };

        foreach (var (body, status, code) in cases)
        {
            var response = await client.PostAsJsonAsync("/transactions/recurrences", body);
            Assert.Equal((status, code), (response.StatusCode, await TitleAsync(response)));
        }

        var noStart = await client.PostAsJsonAsync("/transactions/recurrences", new { accountId, paymentMethod = "Pix", description = "x", category = "y", amount = 1m });
        Assert.Equal("recurrence.starts_on_required", await TitleAsync(noStart));
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/transactions/recurrences")).EnumerateArray());
    }

    [Fact]
    public async Task Edit_WithTheReadVersion_ChangesTheRule_StaleVersionIsAConflict_AndTheAccountNeverChanges()
    {
        var (client, accountId) = (ClientFor(Guid.NewGuid()), Guid.NewGuid());
        var id = (await Json(await client.PostAsJsonAsync("/transactions/recurrences", Rule(accountId)))).GetProperty("id").GetGuid();

        var same = await client.PutAsJsonAsync($"/transactions/recurrences/{id}", Rule(accountId, version: 1));
        var edited = await client.PutAsJsonAsync($"/transactions/recurrences/{id}",
            Rule(accountId, amount: 2150m, endsOn: "2030-12-31", version: 1));
        var stale = await client.PutAsJsonAsync($"/transactions/recurrences/{id}", Rule(accountId, amount: 9m, version: 1));
        var moved = await client.PutAsJsonAsync($"/transactions/recurrences/{id}", Rule(Guid.NewGuid(), version: 2));
        var noVersion = await client.PutAsJsonAsync($"/transactions/recurrences/{id}", Rule(accountId));

        Assert.Equal((HttpStatusCode.OK, 1), (same.StatusCode, (await Json(same)).GetProperty("version").GetInt32()));
        var dto = await Json(edited);
        Assert.Equal((2, 2150m, "2030-12-31"), (dto.GetProperty("version").GetInt32(), dto.GetProperty("amount").GetDecimal(),
            dto.GetProperty("endsOn").GetString()));
        Assert.Equal((HttpStatusCode.Conflict, "recurrence.concurrency_conflict"), (stale.StatusCode, await TitleAsync(stale)));
        Assert.Equal((HttpStatusCode.BadRequest, "recurrence.account_immutable"), (moved.StatusCode, await TitleAsync(moved)));
        Assert.Equal((HttpStatusCode.BadRequest, "recurrence.version_required"), (noVersion.StatusCode, await TitleAsync(noVersion)));

        var events = await EventsForAsync(id);
        Assert.Equal(["RecurrenceSaved", "RecurrenceSaved"], events.Select(e => e.GetProperty("eventType").GetString()));
        Assert.Equal(2150m, events[1].GetProperty("payload").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Delete_RemovesTheRule_AndPublishesRecurrenceDeleted_OtherUsersGet404_ReadScopeIsForbidden()
    {
        var (client, accountId) = (ClientFor(Guid.NewGuid()), Guid.NewGuid());
        var id = (await Json(await client.PostAsJsonAsync("/transactions/recurrences", Rule(accountId)))).GetProperty("id").GetGuid();

        var otherGet = await ClientFor(Guid.NewGuid()).GetAsync($"/transactions/recurrences/{id}");
        var otherDelete = await ClientFor(Guid.NewGuid()).DeleteAsync($"/transactions/recurrences/{id}");
        var readOnly = await ClientFor(Guid.NewGuid(), "expenses.read").PostAsJsonAsync("/transactions/recurrences", Rule(accountId));
        var deleted = await client.DeleteAsync($"/transactions/recurrences/{id}");

        Assert.Equal((HttpStatusCode.NotFound, "recurrence.not_found"), (otherGet.StatusCode, await TitleAsync(otherGet)));
        Assert.Equal(HttpStatusCode.NotFound, otherDelete.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, readOnly.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/transactions/recurrences/{id}")).StatusCode);
        Assert.Equal(["RecurrenceSaved", "RecurrenceDeleted"], (await EventsForAsync(id)).Select(e => e.GetProperty("eventType").GetString()));
    }
}
