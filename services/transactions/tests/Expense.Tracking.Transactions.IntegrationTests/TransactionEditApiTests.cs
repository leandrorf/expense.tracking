using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// LNC-3 (ADR-0007): <c>PUT /transactions/{id}</c> corrige o lancamento inteiro com concorrencia
/// otimista e publica o <c>TransactionUpdated</c> no mesmo commit.
/// </summary>
public sealed class TransactionEditApiTests(TransactionsApiFactory factory) : IClassFixture<TransactionsApiFactory>
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

    private static object Body(Guid accountId, int? version, string category = "Alimentacao", decimal amount = 100m,
        string method = "Pix", Guid? cardId = null, int installments = 1, string? notes = null) => new
    {
        accountId,
        type = "Expense",
        paymentMethod = method,
        creditCardId = cardId,
        description = "Mercado",
        category,
        amount,
        currency = "BRL",
        occurredOn = "2026-11-10",
        notes,
        installments,
        version,
    };

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> TitleAsync(HttpResponseMessage response) => (await Json(response)).GetProperty("title").GetString();

    private async Task<(HttpClient Client, Guid AccountId, Guid Id)> CreatedAsync()
    {
        var (client, accountId) = (ClientFor(Guid.NewGuid()), Guid.NewGuid());
        var created = await Json(await client.PostAsJsonAsync("/transactions", Body(accountId, version: null)));
        Assert.Equal(1, created.GetProperty("version").GetInt32());
        return (client, accountId, created.GetProperty("id").GetGuid());
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
    public async Task Edit_ChangesTheTransaction_BumpsTheVersion_AndPublishesTransactionUpdated()
    {
        var (client, accountId, id) = await CreatedAsync();
        var card = Guid.NewGuid();

        var response = await client.PutAsJsonAsync($"/transactions/{id}",
            Body(accountId, version: 1, category: "Eletronicos", amount: 3000m, method: "CreditCard", cardId: card, installments: 6, notes: "NF 9"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await Json(response);
        Assert.Equal((id, 2, "Eletronicos", 3000m, 6, "NF 9"), (dto.GetProperty("id").GetGuid(), dto.GetProperty("version").GetInt32(),
            dto.GetProperty("category").GetString(), dto.GetProperty("amount").GetDecimal(), dto.GetProperty("installments").GetInt32(),
            dto.GetProperty("notes").GetString()));
        var read = await client.GetFromJsonAsync<JsonElement>($"/transactions/{id}");
        Assert.Equal(("Eletronicos", 2), (read.GetProperty("category").GetString(), read.GetProperty("version").GetInt32()));

        var events = await EventsForAsync(id);
        Assert.Equal(["TransactionRegistered", "TransactionUpdated"], events.Select(e => e.GetProperty("eventType").GetString()));
        var payload = events[1].GetProperty("payload");
        Assert.Equal((accountId, "Eletronicos", 3000m, card, 6, 2, "CreditCard", "2026-11-10"), (
            payload.GetProperty("accountId").GetGuid(), payload.GetProperty("category").GetString(),
            payload.GetProperty("amount").GetDecimal(), payload.GetProperty("creditCardId").GetGuid(),
            payload.GetProperty("installments").GetInt32(), payload.GetProperty("transactionVersion").GetInt32(),
            payload.GetProperty("paymentMethod").GetString(), payload.GetProperty("occurredOn").GetString()));
        Assert.False(payload.TryGetProperty("accountScope", out _));
        // A versao do formato do evento e outra coisa: o TransactionUpdated sai na v1.
        Assert.Equal(1, events[1].GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Edit_WithAStaleVersion_IsAConflict_AndChangesNothing()
    {
        var (client, accountId, id) = await CreatedAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/transactions/{id}", Body(accountId, 1, category: "Feira"))).StatusCode);

        // A outra aba ainda tem a versao 1.
        var stale = await client.PutAsJsonAsync($"/transactions/{id}", Body(accountId, 1, category: "Padaria"));

        Assert.Equal((HttpStatusCode.Conflict, "transaction.concurrency_conflict"), (stale.StatusCode, await TitleAsync(stale)));
        var read = await client.GetFromJsonAsync<JsonElement>($"/transactions/{id}");
        Assert.Equal(("Feira", 2), (read.GetProperty("category").GetString(), read.GetProperty("version").GetInt32()));
    }

    [Fact]
    public async Task Edit_WithoutChanges_KeepsTheVersion_AndPublishesNothing()
    {
        var (client, accountId, id) = await CreatedAsync();

        var same = await client.PutAsJsonAsync($"/transactions/{id}", Body(accountId, 1));

        Assert.Equal((HttpStatusCode.OK, 1), (same.StatusCode, (await Json(same)).GetProperty("version").GetInt32()));
        Assert.Equal(["TransactionRegistered"], (await EventsForAsync(id)).Select(e => e.GetProperty("eventType").GetString()));
    }

    [Fact]
    public async Task Edit_RejectsAnotherAccount_InvalidFields_UnknownCards_AndMissingVersion()
    {
        var (client, accountId, id) = await CreatedAsync();
        var foreignCard = Guid.NewGuid();
        _factory.AccountsClient.InvalidCards.Add(foreignCard);

        var otherAccount = await client.PutAsJsonAsync($"/transactions/{id}", Body(Guid.NewGuid(), 1));
        var invalid = await client.PutAsJsonAsync($"/transactions/{id}", Body(accountId, 1, installments: 3));
        var card = await client.PutAsJsonAsync($"/transactions/{id}", Body(accountId, 1, method: "CreditCard", cardId: foreignCard));
        var noVersion = await client.PutAsJsonAsync($"/transactions/{id}", Body(accountId, null));

        Assert.Equal((HttpStatusCode.BadRequest, "transaction.account_immutable"), (otherAccount.StatusCode, await TitleAsync(otherAccount)));
        Assert.Equal((HttpStatusCode.BadRequest, "transaction.installments_not_allowed"), (invalid.StatusCode, await TitleAsync(invalid)));
        Assert.Equal((HttpStatusCode.NotFound, "card.not_found"), (card.StatusCode, await TitleAsync(card)));
        Assert.Equal((HttpStatusCode.BadRequest, "transaction.version_required"), (noVersion.StatusCode, await TitleAsync(noVersion)));
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>($"/transactions/{id}")).GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Edit_SomeoneElsesTransaction_IsNotFound_AndReadScopeIsForbidden()
    {
        var (_, accountId, id) = await CreatedAsync();

        var other = await ClientFor(Guid.NewGuid()).PutAsJsonAsync($"/transactions/{id}", Body(accountId, 1));
        var readOnly = await ClientFor(Guid.NewGuid(), scope: "expenses.read").PutAsJsonAsync($"/transactions/{id}", Body(accountId, 1));

        Assert.Equal((HttpStatusCode.NotFound, "transaction.not_found"), (other.StatusCode, await TitleAsync(other)));
        Assert.Equal(HttpStatusCode.Forbidden, readOnly.StatusCode);
    }
}
