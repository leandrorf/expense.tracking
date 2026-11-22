using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// INV-6 (ADR-0005, decisao 6): pagamento de fatura em <c>/transactions/card-payments</c>, com os
/// eventos no outbox e sem virar lancamento.
/// </summary>
public sealed class CardPaymentsApiTests(TransactionsApiFactory factory) : IClassFixture<TransactionsApiFactory>
{
    private const string Route = "/transactions/card-payments";
    private readonly TransactionsApiFactory _factory = factory;

    private HttpClient ClientFor(Guid owner, string? scope = null) => AuthenticatedAs(owner.ToString(), scope);

    private HttpClient ServiceClient() => AuthenticatedAs("expense-tracking-service", "events.read");

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

    private static object Payment(Guid cardId, Guid accountId, decimal amount = 1500m, string? month = "2026-12") => new
    {
        creditCardId = cardId,
        invoiceDueMonth = month,
        accountId,
        amount,
        paidOn = "2026-12-15",
    };

    [Fact]
    public async Task Register_StoresThePayment_AndRecordsCardInvoicePaid_WithoutATransaction()
    {
        var owner = Guid.NewGuid();
        var (card, account) = (Guid.NewGuid(), Guid.NewGuid());
        var client = ClientFor(owner);

        var response = await client.PostAsJsonAsync(Route, Payment(card, account, 1234.56m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = dto.GetProperty("id").GetGuid();
        Assert.Equal($"{Route}/{id}", response.Headers.Location!.ToString());
        Assert.Equal(("2026-12", 1234.56m, "BRL", "2026-12-15"), (dto.GetProperty("invoiceDueMonth").GetString(),
            dto.GetProperty("amount").GetDecimal(), dto.GetProperty("currency").GetString(), dto.GetProperty("paidOn").GetString()));

        // Nao e lancamento: a lista de lancamentos do dono continua vazia.
        Assert.Empty(await client.GetFromJsonAsync<JsonElement[]>("/transactions") ?? []);

        var paid = Assert.Single(await EventsForAsync(id));
        Assert.Equal(("CardInvoicePaid", 1, owner), (paid.GetProperty("eventType").GetString(),
            paid.GetProperty("version").GetInt32(), paid.GetProperty("ownerUserId").GetGuid()));
        var payload = paid.GetProperty("payload");
        Assert.Equal((id, card, account, "2026-12", 1234.56m, "BRL", "2026-12-15"), (
            payload.GetProperty("paymentId").GetGuid(), payload.GetProperty("creditCardId").GetGuid(),
            payload.GetProperty("accountId").GetGuid(), payload.GetProperty("invoiceDueMonth").GetString(),
            payload.GetProperty("amount").GetDecimal(), payload.GetProperty("currency").GetString(),
            payload.GetProperty("paidOn").GetString()));
        Assert.False(payload.TryGetProperty("transactionId", out _));
    }

    [Fact]
    public async Task List_FiltersByCardAndMonth_OnlyForTheOwner()
    {
        var owner = Guid.NewGuid();
        var (card, otherCard, account) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var client = ClientFor(owner);
        await client.PostAsJsonAsync(Route, Payment(card, account, 100m, "2026-12"));
        await client.PostAsJsonAsync(Route, Payment(card, account, 200m, "2027-01"));
        await client.PostAsJsonAsync(Route, Payment(otherCard, account, 300m, "2026-12"));
        await ClientFor(Guid.NewGuid()).PostAsJsonAsync(Route, Payment(card, account, 400m, "2026-12"));

        var all = await client.GetFromJsonAsync<JsonElement[]>(Route);
        var ofCard = await client.GetFromJsonAsync<JsonElement[]>($"{Route}?creditCardId={card}");
        var ofInvoice = await client.GetFromJsonAsync<JsonElement[]>($"{Route}?creditCardId={card}&invoiceDueMonth=2026-12");
        var badMonth = await client.GetAsync($"{Route}?invoiceDueMonth=12/2026");

        Assert.Equal(3, all!.Length);
        Assert.Equal([100m, 200m], ofCard!.Select(p => p.GetProperty("amount").GetDecimal()).Order());
        Assert.Equal(100m, Assert.Single(ofInvoice!).GetProperty("amount").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest, badMonth.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesThePayment_AndRecordsCardInvoicePaymentDeleted()
    {
        var owner = Guid.NewGuid();
        var card = Guid.NewGuid();
        var client = ClientFor(owner);
        var created = await (await client.PostAsJsonAsync(Route, Payment(card, Guid.NewGuid())))
            .Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var byStranger = await ClientFor(Guid.NewGuid()).DeleteAsync($"{Route}/{id}");
        var delete = await client.DeleteAsync($"{Route}/{id}");
        var again = await client.DeleteAsync($"{Route}/{id}");

        Assert.Equal(HttpStatusCode.NotFound, byStranger.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Empty(await client.GetFromJsonAsync<JsonElement[]>(Route) ?? []);

        var events = await EventsForAsync(id);
        Assert.Equal(["CardInvoicePaid", "CardInvoicePaymentDeleted"], events.Select(e => e.GetProperty("eventType").GetString()));
        var payload = events[1].GetProperty("payload");
        Assert.Equal((id, owner, card), (payload.GetProperty("paymentId").GetGuid(),
            payload.GetProperty("ownerUserId").GetGuid(), payload.GetProperty("creditCardId").GetGuid()));
    }

    [Fact]
    public async Task AnotherUsersCardOrAccount_IsNotFound_AndInactiveAccountRejected()
    {
        var (foreignCard, foreignAccount, inactive) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _factory.AccountsClient.InvalidCards.Add(foreignCard);
        _factory.AccountsClient.InvalidAccounts.Add(foreignAccount);
        _factory.AccountsClient.InactiveAccounts.Add(inactive);
        var client = ClientFor(Guid.NewGuid());

        var card = await client.PostAsJsonAsync(Route, Payment(foreignCard, Guid.NewGuid()));
        var account = await client.PostAsJsonAsync(Route, Payment(Guid.NewGuid(), foreignAccount));
        var inactiveAccount = await client.PostAsJsonAsync(Route, Payment(Guid.NewGuid(), inactive));

        Assert.Equal((HttpStatusCode.NotFound, "card.not_found"), (card.StatusCode, await CodeOf(card)));
        Assert.Equal((HttpStatusCode.NotFound, "account.not_found"), (account.StatusCode, await CodeOf(account)));
        Assert.Equal((HttpStatusCode.BadRequest, "card_payment.account_inactive"), (inactiveAccount.StatusCode, await CodeOf(inactiveAccount)));
    }

    [Theory]
    [InlineData("12-2026", 100, "card_payment.month_invalid")]
    [InlineData(null, 100, "card_payment.month_invalid")]
    [InlineData("2026-12", 0, "money.amount_invalid")]
    [InlineData("2026-12", -5, "money.amount_invalid")]
    public async Task InvalidBody_IsBadRequest(string? month, decimal amount, string code)
    {
        var response = await ClientFor(Guid.NewGuid()).PostAsJsonAsync(Route, Payment(Guid.NewGuid(), Guid.NewGuid(), amount, month));

        Assert.Equal((HttpStatusCode.BadRequest, code), (response.StatusCode, await CodeOf(response)));
    }

    [Fact]
    public async Task Scopes_ReadCanList_ButNotRegisterOrDelete_AndAnonymousIsUnauthorized()
    {
        var reader = ClientFor(Guid.NewGuid(), "expenses.read");

        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(Route, Payment(Guid.NewGuid(), Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.DeleteAsync($"{Route}/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(Route)).StatusCode);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString();

    private async Task<List<JsonElement>> EventsForAsync(Guid aggregateId)
    {
        var service = ServiceClient();
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
            after = page.GetProperty("lastSequence").GetInt64();
        }
    }
}
