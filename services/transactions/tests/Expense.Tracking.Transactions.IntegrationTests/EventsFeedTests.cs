using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Expense.Tracking.Transactions.Domain.Transactions;
using Expense.Tracking.Transactions.Infrastructure.Persistence;
using Expense.Tracking.Transactions.Infrastructure.Persistence.Outbox;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// EVT-1 (ADR-0001): outbox transacional e feed <c>GET /events</c>. Os eventos de outros testes
/// da classe dividem o banco, entao as verificacoes filtram pelo lancamento de cada teste.
/// </summary>
public sealed partial class EventsFeedTests(TransactionsApiFactory factory) : IClassFixture<TransactionsApiFactory>
{
    private const string ServiceClientSub = "expense-tracking-service";
    private readonly TransactionsApiFactory _factory = factory;

    [Fact]
    public async Task Feed_RequiresTheEventsReadScope()
    {
        var anonymous = await _factory.CreateClient().GetAsync("/events");
        var user = await UserClient(Guid.NewGuid()).GetAsync("/events");
        var service = await ServiceClient().GetAsync("/events");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, user.StatusCode);
        Assert.Equal(HttpStatusCode.OK, service.StatusCode);
    }

    [Fact]
    public async Task CreateTransaction_RecordsTransactionRegistered_WithTheAccountScope()
    {
        var owner = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        _factory.AccountsClient.Scopes[accountId] = "Business";

        var transactionId = await CreateTransactionAsync(owner, accountId, amount: 250.75m);

        var events = EventsFor(await ReadAllEventsAsync(ServiceClient()), transactionId);
        var registered = Assert.Single(events);
        Assert.Equal("TransactionRegistered", registered.GetProperty("eventType").GetString());
        Assert.Equal(2, registered.GetProperty("version").GetInt32());
        Assert.Equal(owner, registered.GetProperty("ownerUserId").GetGuid());
        Assert.NotEqual(Guid.Empty, registered.GetProperty("eventId").GetGuid());
        Assert.Matches(TraceParentPattern(), registered.GetProperty("traceParent").GetString());

        var payload = registered.GetProperty("payload");
        Assert.Equal(transactionId, payload.GetProperty("transactionId").GetGuid());
        Assert.Equal(accountId, payload.GetProperty("accountId").GetGuid());
        Assert.Equal("Business", payload.GetProperty("accountScope").GetString());
        Assert.Equal("Expense", payload.GetProperty("transactionType").GetString());
        Assert.Equal("Pix", payload.GetProperty("paymentMethod").GetString());
        Assert.Equal("Alimentacao", payload.GetProperty("category").GetString());
        Assert.Equal(250.75m, payload.GetProperty("amount").GetDecimal());
        Assert.Equal("BRL", payload.GetProperty("currency").GetString());
        Assert.Equal("2026-01-15", payload.GetProperty("occurredOn").GetString());
        // v2 (INV-2): descricao, cartao (nulo fora do cartao) e parcelas.
        Assert.Equal("Almoco", payload.GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("creditCardId").ValueKind);
        Assert.Equal(1, payload.GetProperty("installments").GetInt32());
    }

    [Fact]
    public async Task CreateCardPurchase_RecordsTheCardInTheV2Event()
    {
        var owner = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var response = await UserClient(owner).PostAsJsonAsync("/transactions", new
        {
            accountId, type = "Expense", paymentMethod = "CreditCard", creditCardId = cardId, description = "Notebook",
            category = "Eletronicos", amount = 3000m, currency = "BRL", occurredOn = "2026-11-20",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transactionId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var payload = Assert.Single(EventsFor(await ReadAllEventsAsync(ServiceClient()), transactionId)).GetProperty("payload");

        Assert.Equal(cardId, payload.GetProperty("creditCardId").GetGuid());
        Assert.Equal(1, payload.GetProperty("installments").GetInt32());
        Assert.Equal("Notebook", payload.GetProperty("description").GetString());
    }

    [Fact]
    public async Task CardDetailsBackfill_CompletesCardPurchasesWithAV1Event_Once()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>();
        var cardId = Guid.NewGuid();
        Transaction NewTransaction(PaymentMethod method, Guid? card, string description) => Transaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), TransactionType.Expense, method, card,
            description, "Mercado", 80m, "BRL", new DateOnly(2025, 12, 1), null, DateTimeOffset.UtcNow).Value;
        var legacyCard = NewTransaction(PaymentMethod.CreditCard, cardId, "Compra antiga");
        var legacyPix = NewTransaction(PaymentMethod.Pix, null, "Pix antigo");
        db.Transactions.AddRange(legacyCard, legacyPix);
        // Eventos v1, como gravados antes da INV-2.
        foreach (var legacy in new[] { legacyCard, legacyPix })
        {
            db.OutboxEvents.Add(new OutboxEvent
            {
                EventId = Guid.NewGuid(), EventType = "TransactionRegistered", Version = 1, AggregateId = legacy.Id,
                OwnerUserId = legacy.OwnerUserId, OccurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5), Payload = "{}",
            });
        }

        await db.SaveChangesAsync();
        // Compra no cartao ja com evento v2: nao precisa de backfill.
        var current = await CreateCardPurchaseAsync();

        var first = await CardDetailsBackfill.RunAsync(db, TimeProvider.System);
        var second = await CardDetailsBackfill.RunAsync(db, TimeProvider.System);

        Assert.True(first >= 1);
        Assert.Equal(0, second);
        var events = await ReadAllEventsAsync(ServiceClient());
        var resolved = Assert.Single(EventsFor(events, legacyCard.Id), e => e.GetProperty("eventType").GetString() == "TransactionCardDetailsResolved");
        var payload = resolved.GetProperty("payload");
        Assert.Equal((cardId, 1, "Compra antiga"),
            (payload.GetProperty("creditCardId").GetGuid(), payload.GetProperty("installments").GetInt32(), payload.GetProperty("description").GetString()));
        Assert.DoesNotContain(EventsFor(events, legacyPix.Id), e => e.GetProperty("eventType").GetString() == "TransactionCardDetailsResolved");
        Assert.DoesNotContain(EventsFor(events, current), e => e.GetProperty("eventType").GetString() == "TransactionCardDetailsResolved");
    }

    private async Task<Guid> CreateCardPurchaseAsync()
    {
        var response = await UserClient(Guid.NewGuid()).PostAsJsonAsync("/transactions", new
        {
            accountId = Guid.NewGuid(), type = "Expense", paymentMethod = "CreditCard", creditCardId = Guid.NewGuid(),
            description = "Atual", category = "X", amount = 10m, currency = "BRL", occurredOn = "2026-11-20",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task CreateTransaction_ForAnAccountThatDoesNotExist_RecordsNoEvent()
    {
        var accountId = Guid.NewGuid();
        _factory.AccountsClient.InvalidAccounts.Add(accountId);

        var response = await UserClient(Guid.NewGuid()).PostAsJsonAsync("/transactions", NewTransaction(accountId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var events = await ReadAllEventsAsync(ServiceClient());
        Assert.DoesNotContain(events, e =>
            e.GetProperty("payload").TryGetProperty("accountId", out var a) && a.GetGuid() == accountId);
    }

    [Fact]
    public async Task DeleteTransaction_RecordsTransactionDeleted_AfterTheRegistration()
    {
        var owner = Guid.NewGuid();
        var transactionId = await CreateTransactionAsync(owner, Guid.NewGuid());

        var delete = await UserClient(owner).DeleteAsync($"/transactions/{transactionId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var events = EventsFor(await ReadAllEventsAsync(ServiceClient()), transactionId);
        Assert.Equal(["TransactionRegistered", "TransactionDeleted"], events.Select(e => e.GetProperty("eventType").GetString()));
        Assert.True(events[1].GetProperty("sequence").GetInt64() > events[0].GetProperty("sequence").GetInt64());

        var payload = events[1].GetProperty("payload");
        Assert.Equal(transactionId, payload.GetProperty("transactionId").GetGuid());
        Assert.Equal(owner, payload.GetProperty("ownerUserId").GetGuid());
    }

    [Fact]
    public async Task Feed_PagesInSequenceOrder_UsingLastSequenceAsTheCursor()
    {
        var service = ServiceClient();
        var start = (await ReadPageAsync(service, after: 0, limit: ReadLimit)).GetProperty("lastSequence").GetInt64();
        start = await DrainAsync(service, start);

        var created = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            created.Add(await CreateTransactionAsync(Guid.NewGuid(), Guid.NewGuid()));
        }

        var cursor = start;
        var seen = new List<(long Sequence, Guid Aggregate)>();
        for (var i = 0; i < 3; i++)
        {
            var page = await ReadPageAsync(service, cursor, limit: 1);
            var single = Assert.Single(page.GetProperty("events").EnumerateArray());
            var sequence = single.GetProperty("sequence").GetInt64();
            Assert.Equal(sequence, page.GetProperty("lastSequence").GetInt64());
            // Os 3 lancamentos ja estao gravados: o head nao fica para tras do ultimo.
            Assert.True(page.GetProperty("headSequence").GetInt64() >= start + 3);
            seen.Add((sequence, single.GetProperty("aggregateId").GetGuid()));
            cursor = sequence;
        }

        Assert.Equal(created, seen.Select(s => s.Aggregate));
        Assert.True(seen.Zip(seen.Skip(1)).All(p => p.Second.Sequence > p.First.Sequence));

        var end = await ReadPageAsync(service, cursor, limit: 10);
        Assert.Empty(end.GetProperty("events").EnumerateArray());
        Assert.Equal(cursor, end.GetProperty("lastSequence").GetInt64());
        Assert.True(end.GetProperty("headSequence").GetInt64() >= cursor);
    }

    [Theory]
    [InlineData("after=-1")]
    [InlineData("limit=0")]
    [InlineData("limit=501")]
    public async Task Feed_RejectsInvalidParameters(string query)
    {
        var response = await ServiceClient().GetAsync($"/events?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Backfill_RegistersTransactionsThatPredateTheOutbox_Once()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>();
        var legacy = Transaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), TransactionType.Expense, PaymentMethod.Cash, null,
            "Antigo", "Mercado", 80m, "BRL", new DateOnly(2025, 12, 1), null, DateTimeOffset.UtcNow).Value;
        db.Transactions.Add(legacy);
        await db.SaveChangesAsync();

        var first = await OutboxBackfill.RunAsync(db, TimeProvider.System);
        var second = await OutboxBackfill.RunAsync(db, TimeProvider.System);

        Assert.True(first >= 1);
        Assert.Equal(0, second);
        var registered = Assert.Single(EventsFor(await ReadAllEventsAsync(ServiceClient()), legacy.Id));
        // O transactions nao conhece o escopo de lancamentos anteriores ao outbox.
        Assert.Equal(JsonValueKind.Null, registered.GetProperty("payload").GetProperty("accountScope").ValueKind);
    }

    private const int ReadLimit = 500;

    private HttpClient UserClient(Guid owner) => AuthenticatedAs(_factory.CreateClient(), owner.ToString());

    private HttpClient ServiceClient() => AuthenticatedAs(_factory.CreateClient(), ServiceClientSub, "events.read");

    private static HttpClient AuthenticatedAs(HttpClient client, string sub, string? scope = null)
    {
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, sub);
        if (scope is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, scope);
        }

        return client;
    }

    private static object NewTransaction(Guid accountId, decimal amount = 42m) => new
    {
        accountId,
        type = "Expense",
        paymentMethod = "Pix",
        description = "Almoco",
        category = "Alimentacao",
        amount,
        currency = "BRL",
        occurredOn = "2026-01-15",
    };

    private async Task<Guid> CreateTransactionAsync(Guid owner, Guid accountId, decimal amount = 42m)
    {
        var response = await UserClient(owner).PostAsJsonAsync("/transactions", NewTransaction(accountId, amount));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ReadPageAsync(HttpClient client, long after, int limit)
    {
        var response = await client.GetAsync($"/events?after={after}&limit={limit}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<long> DrainAsync(HttpClient client, long after)
    {
        while (true)
        {
            var page = await ReadPageAsync(client, after, ReadLimit);
            var next = page.GetProperty("lastSequence").GetInt64();
            if (next == after)
            {
                return after;
            }

            after = next;
        }
    }

    private static async Task<List<JsonElement>> ReadAllEventsAsync(HttpClient client)
    {
        var all = new List<JsonElement>();
        long after = 0;
        while (true)
        {
            var page = await ReadPageAsync(client, after, ReadLimit);
            var events = page.GetProperty("events").EnumerateArray().ToArray();
            if (events.Length == 0)
            {
                return all;
            }

            all.AddRange(events);
            after = page.GetProperty("lastSequence").GetInt64();
        }
    }

    private static List<JsonElement> EventsFor(IEnumerable<JsonElement> events, Guid transactionId) =>
        events.Where(e => e.GetProperty("aggregateId").GetGuid() == transactionId).ToList();

    // W3C Trace Context: versao-traceid-spanid-flags (o .NET 10 tambem liga o bit "random", ex.: 03).
    [GeneratedRegex("^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$")]
    private static partial Regex TraceParentPattern();
}
