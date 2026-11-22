using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// LNC-7: <c>POST /transactions/recategorize</c> troca a categoria de varios lancamentos, um
/// <c>TransactionUpdated</c> por lancamento, e informa (sem sobrescrever) os que mudaram em outra aba.
/// </summary>
public sealed class TransactionRecategorizeApiTests(TransactionsApiFactory factory) : IClassFixture<TransactionsApiFactory>
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

    private static object Body(Guid accountId, string category, string description = "Mercado", int? version = null) => new
    {
        accountId,
        type = "Expense",
        paymentMethod = "Pix",
        description,
        category,
        amount = 50m,
        currency = "BRL",
        occurredOn = "2026-11-10",
        installments = 1,
        version,
    };

    private static object Batch(string category, params (Guid Id, int Version)[] items) => new
    {
        category,
        items = items.Select(i => new { id = i.Id, version = i.Version }).ToArray(),
    };

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> TitleAsync(HttpResponseMessage response) => (await Json(response)).GetProperty("title").GetString();

    private static Guid[] Ids(JsonElement result, string list) =>
        result.GetProperty(list).EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetGuid() : e.GetProperty("id").GetGuid())
            .Order().ToArray();

    private static async Task<Guid> CreateAsync(HttpClient client, Guid accountId, string category, string description = "Mercado") =>
        (await Json(await client.PostAsJsonAsync("/transactions", Body(accountId, category, description)))).GetProperty("id").GetGuid();

    private async Task<string[]> EventTypesForAsync(Guid aggregateId)
    {
        var service = AuthenticatedAs("expense-tracking-service", "events.read");
        var found = new List<string>();
        long after = 0;
        while (true)
        {
            var page = await service.GetFromJsonAsync<JsonElement>($"/events?after={after}&limit=500");
            var events = page.GetProperty("events").EnumerateArray().ToArray();
            if (events.Length == 0)
            {
                return [.. found];
            }

            found.AddRange(events.Where(e => e.GetProperty("aggregateId").GetGuid() == aggregateId)
                .Select(e => e.GetProperty("eventType").GetString() ?? string.Empty));
            after = events[^1].GetProperty("sequence").GetInt64();
        }
    }

    [Fact]
    public async Task Recategorize_ChangesEverySelectedTransaction_WithOneEventEach()
    {
        var (client, accountId) = (ClientFor(Guid.NewGuid()), Guid.NewGuid());
        var a = await CreateAsync(client, accountId, "Outros", "Padaria");
        var b = await CreateAsync(client, accountId, "Outros", "Feira");
        var same = await CreateAsync(client, accountId, "Alimentacao", "Acougue");

        var response = await client.PostAsJsonAsync("/transactions/recategorize", Batch(" Alimentacao ", (a, 1), (b, 1), (same, 1)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await Json(response);
        Assert.Equal(new[] { a, b }.Order(), Ids(result, "updated"));
        Assert.Equal([same], Ids(result, "unchanged"));
        Assert.Empty(Ids(result, "conflicts"));
        Assert.Empty(Ids(result, "notFound"));
        Assert.All(result.GetProperty("updated").EnumerateArray(), dto =>
            Assert.Equal(("Alimentacao", 2), (dto.GetProperty("category").GetString(), dto.GetProperty("version").GetInt32())));

        var readA = await client.GetFromJsonAsync<JsonElement>($"/transactions/{a}");
        Assert.Equal(("Alimentacao", "Padaria", 50m), (readA.GetProperty("category").GetString(),
            readA.GetProperty("description").GetString(), readA.GetProperty("amount").GetDecimal()));
        Assert.Equal(["TransactionRegistered", "TransactionUpdated"], await EventTypesForAsync(a));
        Assert.Equal(["TransactionRegistered", "TransactionUpdated"], await EventTypesForAsync(b));
        Assert.Equal(["TransactionRegistered"], await EventTypesForAsync(same));
    }

    [Fact]
    public async Task Recategorize_ReportsWhatChangedElsewhere_AndWhatIsNotMine_WithoutOverwriting()
    {
        var (client, accountId) = (ClientFor(Guid.NewGuid()), Guid.NewGuid());
        var fresh = await CreateAsync(client, accountId, "Outros");
        var stale = await CreateAsync(client, accountId, "Outros");
        // Outra aba corrigiu "stale" depois que a lista foi lida (versao 1 -> 2).
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"/transactions/{stale}", Body(accountId, "Feira", version: 1))).StatusCode);
        var foreign = await CreateAsync(ClientFor(Guid.NewGuid()), Guid.NewGuid(), "Outros");
        var missing = Guid.NewGuid();

        var result = await Json(await client.PostAsJsonAsync("/transactions/recategorize",
            Batch("Alimentacao", (fresh, 1), (stale, 1), (foreign, 1), (missing, 1))));

        Assert.Equal([fresh], Ids(result, "updated"));
        Assert.Equal([stale], Ids(result, "conflicts"));
        Assert.Equal(new[] { foreign, missing }.Order(), Ids(result, "notFound"));
        var conflict = result.GetProperty("conflicts")[0];
        Assert.Equal(("Feira", 2), (conflict.GetProperty("category").GetString(), conflict.GetProperty("version").GetInt32()));
        Assert.Equal("Feira", (await client.GetFromJsonAsync<JsonElement>($"/transactions/{stale}")).GetProperty("category").GetString());
    }

    [Fact]
    public async Task Recategorize_RejectsAnInvalidCategory_AnEmptyBatch_AndRepeatedIds()
    {
        var (client, accountId) = (ClientFor(Guid.NewGuid()), Guid.NewGuid());
        var id = await CreateAsync(client, accountId, "Outros");

        var noCategory = await client.PostAsJsonAsync("/transactions/recategorize", Batch("  ", (id, 1)));
        var tooLong = await client.PostAsJsonAsync("/transactions/recategorize", Batch(new string('x', 61), (id, 1)));
        var empty = await client.PostAsJsonAsync("/transactions/recategorize", Batch("Alimentacao"));
        var repeated = await client.PostAsJsonAsync("/transactions/recategorize", Batch("Alimentacao", (id, 1), (id, 1)));

        Assert.Equal((HttpStatusCode.BadRequest, "transaction.category_required"), (noCategory.StatusCode, await TitleAsync(noCategory)));
        Assert.Equal((HttpStatusCode.BadRequest, "transaction.category_too_long"), (tooLong.StatusCode, await TitleAsync(tooLong)));
        Assert.Equal((HttpStatusCode.BadRequest, "transaction.batch_empty"), (empty.StatusCode, await TitleAsync(empty)));
        Assert.Equal((HttpStatusCode.BadRequest, "transaction.batch_duplicated"), (repeated.StatusCode, await TitleAsync(repeated)));
        Assert.Equal(("Outros", 1), ((await client.GetFromJsonAsync<JsonElement>($"/transactions/{id}")).GetProperty("category").GetString(),
            (await client.GetFromJsonAsync<JsonElement>($"/transactions/{id}")).GetProperty("version").GetInt32()));
    }

    [Fact]
    public async Task Recategorize_WithReadScope_IsForbidden()
    {
        var response = await ClientFor(Guid.NewGuid(), scope: "expenses.read")
            .PostAsJsonAsync("/transactions/recategorize", Batch("Alimentacao", (Guid.NewGuid(), 1)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
