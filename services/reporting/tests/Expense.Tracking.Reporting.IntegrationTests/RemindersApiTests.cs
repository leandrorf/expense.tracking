using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Reporting.IntegrationTests;

/// <summary>
/// NTF-2 (ADR-0009): <c>GET /reports/reminders</c>. Hoje = 2026-11-21 (relogio parado do
/// <see cref="InvoicesApiFactory"/>); antecedencia padrao de 3 dias.
/// </summary>
public sealed class RemindersApiTests(InvoicesApiFactory factory) : IClassFixture<InvoicesApiFactory>
{
    private readonly InvoicesApiFactory _factory = factory;

    private HttpClient ClientFor(Guid owner)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, owner.ToString());
        return client;
    }

    [Fact]
    public async Task Reminders_ListInvoicesDueOrOverdue_ExpenseRecurrencesDueSoon_AndWhatIsLeftToLaunch()
    {
        var owner = Guid.NewGuid();
        var feed = _factory.EventFeed;
        // Fecha 15, vence 23: a compra de 10/11 esta numa fatura fechada que vence em 2 dias; 300 ja pagos.
        var soon = _factory.Cards.Add(owner, closingDay: 15, dueDay: 23);
        feed.CardPurchase(Guid.NewGuid(), owner, soon.Id, 800m, "2026-11-10");
        feed.InvoicePaid(Guid.NewGuid(), owner, soon.Id, "2026-11", 300m, "2026-11-20");
        // Fecha 1, vence 5: a compra de 20/10 venceu em 05/11 sem pagamento.
        var late = _factory.Cards.Add(owner, closingDay: 1, dueDay: 5);
        feed.CardPurchase(Guid.NewGuid(), owner, late.Id, 150m, "2026-10-20");
        // Aluguel vence em 23/11 (avisa); salario em 22/11 e receita (nao avisa); academia de 10/11 ficou a lancar.
        var rent = Guid.NewGuid();
        feed.RecurrenceSaved(rent, owner, 2000m, "2026-11-23", description: "Aluguel");
        feed.RecurrenceSaved(Guid.NewGuid(), owner, 5000m, "2026-11-22", transactionType: "Income", paymentMethod: "Pix", description: "Salario");
        feed.RecurrenceSaved(Guid.NewGuid(), owner, 120m, "2026-11-10", paymentMethod: "Pix", description: "Academia");
        // Outro usuario nao aparece.
        feed.RecurrenceSaved(Guid.NewGuid(), Guid.NewGuid(), 99m, "2026-11-22");
        await _factory.DrainFeedAsync();

        var body = await ClientFor(owner).GetFromJsonAsync<JsonElement>("/reports/reminders");

        Assert.Equal((3, 2), (body.GetProperty("leadDays").GetInt32(), body.GetProperty("overdue").GetInt32()));
        var items = body.GetProperty("items").EnumerateArray()
            .Select(i => (i.GetProperty("kind").GetString(), i.GetProperty("urgency").GetString(), i.GetProperty("amount").GetDecimal(),
                i.GetProperty("dueOn").GetString()))
            .ToList();
        Assert.Equal(
            [("InvoiceOverdue", "Overdue", 150m, "2026-11-05"),
             ("RecurrencesToLaunch", "Overdue", 0m, "2026-11-10"),
             ("RecurrenceDue", "Soon", 2000m, "2026-11-23"),
             ("InvoiceDue", "Soon", 500m, "2026-11-23")],
            items);
        var first = body.GetProperty("items")[1];
        Assert.Equal(1, first.GetProperty("count").GetInt32());
        var invoice = body.GetProperty("items")[3];
        Assert.Equal((soon.Id, "2026-11"), (invoice.GetProperty("cardId").GetGuid(), invoice.GetProperty("dueMonth").GetString()));
        var recurrence = body.GetProperty("items")[2];
        Assert.Equal((rent, "2026-11", "Aluguel"), (recurrence.GetProperty("recurrenceId").GetGuid(),
            recurrence.GetProperty("period").GetString(), recurrence.GetProperty("title").GetString()));
    }

    private HttpClient WriterFor(Guid owner)
    {
        var client = ClientFor(owner);
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, "expenses.read expenses.write");
        return client;
    }

    [Fact]
    public async Task Dismissing_HidesThatReminder_OnlyForItsOwner_AndBadKeysAreRefused()
    {
        // NTF-5 (ADR-0009, decisao 5).
        var owner = Guid.NewGuid();
        var feed = _factory.EventFeed;
        var card = _factory.Cards.Add(owner, closingDay: 15, dueDay: 23);
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 800m, "2026-11-10");
        var rent = Guid.NewGuid();
        feed.RecurrenceSaved(rent, owner, 2000m, "2026-11-23");
        await _factory.DrainFeedAsync();
        var client = WriterFor(owner);
        var keys = (await client.GetFromJsonAsync<JsonElement>("/reports/reminders")).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("key").GetString()!).ToList();
        Assert.Equal([$"recurrence:{rent}:2026-11", $"invoice:{card.Id}:2026-11:due"], keys);

        var dismissed = await client.PostAsJsonAsync("/reports/reminders/dismissals", new { key = keys[1] });
        var twice = await client.PostAsJsonAsync("/reports/reminders/dismissals", new { key = keys[1] });
        var garbage = await client.PostAsJsonAsync("/reports/reminders/dismissals", new { key = "qualquer-coisa" });
        var empty = await client.PostAsJsonAsync("/reports/reminders/dismissals", new { key = " " });
        var readOnly = await ClientFor(owner).PostAsJsonAsync("/reports/reminders/dismissals", new { key = keys[0] });

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (dismissed.StatusCode, twice.StatusCode));
        Assert.Equal(HttpStatusCode.BadRequest, garbage.StatusCode);
        Assert.Equal("reminders.key_invalid", (await empty.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, readOnly.StatusCode);
        var remaining = (await client.GetFromJsonAsync<JsonElement>("/reports/reminders")).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("key").GetString()).ToList();
        Assert.Equal([keys[0]], remaining);

        // A dispensa de outro usuario com a mesma chave nao muda nada aqui (e nem o contrario).
        await WriterFor(Guid.NewGuid()).PostAsJsonAsync("/reports/reminders/dismissals", new { key = keys[0] });
        Assert.Single((await client.GetFromJsonAsync<JsonElement>("/reports/reminders")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Settings_ChangeTheLeadDays_AndTurnKindsOff()
    {
        // NTF-6 (ADR-0009, decisao 4). Fecha 15, vence 27: a fatura vence em 6 dias.
        var owner = Guid.NewGuid();
        var feed = _factory.EventFeed;
        var card = _factory.Cards.Add(owner, closingDay: 15, dueDay: 27);
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 400m, "2026-11-10");
        feed.RecurrenceSaved(Guid.NewGuid(), owner, 2000m, "2026-11-23");
        await _factory.DrainFeedAsync();
        var client = WriterFor(owner);
        async Task<List<string?>> KindsAsync() =>
            (await client.GetFromJsonAsync<JsonElement>("/reports/reminders")).GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("kind").GetString()).ToList();

        var defaults = await client.GetFromJsonAsync<JsonElement>("/reports/reminder-settings");
        Assert.Equal((3, true, true, true), (defaults.GetProperty("leadDays").GetInt32(), defaults.GetProperty("invoices").GetBoolean(),
            defaults.GetProperty("recurrences").GetBoolean(), defaults.GetProperty("budgets").GetBoolean()));
        Assert.Equal(["RecurrenceDue"], await KindsAsync());

        var saved = await client.PutAsJsonAsync("/reports/reminder-settings", new { leadDays = 7, invoices = true, recurrences = false, budgets = true });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(["InvoiceDue"], await KindsAsync());
        var body = await client.GetFromJsonAsync<JsonElement>("/reports/reminders");
        Assert.Equal(7, body.GetProperty("leadDays").GetInt32());

        var tooFar = await client.PutAsJsonAsync("/reports/reminder-settings", new { leadDays = 16, invoices = true, recurrences = true, budgets = true });
        var negative = await client.PutAsJsonAsync("/reports/reminder-settings", new { leadDays = -1, invoices = true, recurrences = true, budgets = true });
        var readOnly = await ClientFor(owner).PutAsJsonAsync("/reports/reminder-settings", new { leadDays = 1, invoices = true, recurrences = true, budgets = true });
        Assert.Equal("reminders.lead_days_invalid", (await tooFar.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, readOnly.StatusCode);
        Assert.Equal(7, (await client.GetFromJsonAsync<JsonElement>("/reports/reminder-settings")).GetProperty("leadDays").GetInt32());
    }

    [Fact]
    public async Task Budgets_InWarningOrExceeded_AreReminders_AndCanBeTurnedOff()
    {
        // NTF-9: mes corrente = 2026-11; limiar de atencao padrao de 80%.
        var owner = Guid.NewGuid();
        var client = WriterFor(owner);
        object Budget(string category, decimal amount) => new
        {
            category, scope = "Personal", amount, currency = "BRL", startMonth = "2026-11",
        };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/budgets", Budget("Mercado", 1000m))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/budgets", Budget("Lazer", 100m))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/budgets", Budget("Saude", 1000m))).StatusCode);
        var feed = _factory.EventFeed;
        feed.Registered(Guid.NewGuid(), owner, "Personal", 900m, "Mercado", occurredOn: "2026-11-10");
        feed.Registered(Guid.NewGuid(), owner, "Personal", 150m, "Lazer", occurredOn: "2026-11-12");
        feed.Registered(Guid.NewGuid(), owner, "Personal", 100m, "Saude", occurredOn: "2026-11-12");
        await _factory.DrainFeedAsync();

        var items = (await client.GetFromJsonAsync<JsonElement>("/reports/reminders")).GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("kind").GetString() == "Budget")
            .Select(i => (i.GetProperty("title").GetString(), i.GetProperty("urgency").GetString(), i.GetProperty("amount").GetDecimal(),
                i.GetProperty("limit").GetDecimal(), i.GetProperty("dueOn").GetString()))
            .ToList();

        Assert.Equal(
            [("Orçamento Lazer (PF)", "Overdue", 150m, 100m, "2026-11-30"),
             ("Orçamento Mercado (PF)", "Soon", 900m, 1000m, "2026-11-30")],
            items);

        await client.PutAsJsonAsync("/reports/reminder-settings", new { leadDays = 3, invoices = true, recurrences = true, budgets = false });
        Assert.DoesNotContain((await client.GetFromJsonAsync<JsonElement>("/reports/reminders")).GetProperty("items").EnumerateArray(),
            i => i.GetProperty("kind").GetString() == "Budget");
    }

    [Fact]
    public async Task Reminders_DisappearWhenResolved_AndRequireAToken()
    {
        var owner = Guid.NewGuid();
        var feed = _factory.EventFeed;
        var card = _factory.Cards.Add(owner, closingDay: 15, dueDay: 23);
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 800m, "2026-11-10");
        var rent = Guid.NewGuid();
        feed.RecurrenceSaved(rent, owner, 2000m, "2026-11-23");
        await _factory.DrainFeedAsync();
        Assert.Equal(2, (await ClientFor(owner).GetFromJsonAsync<JsonElement>("/reports/reminders")).GetProperty("items").GetArrayLength());

        feed.InvoicePaid(Guid.NewGuid(), owner, card.Id, "2026-11", 800m, "2026-11-21");
        feed.OccurrenceClosed(rent, owner, "2026-11", Guid.NewGuid());
        await _factory.DrainFeedAsync();

        Assert.Equal(0, (await ClientFor(owner).GetFromJsonAsync<JsonElement>("/reports/reminders")).GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/reports/reminders")).StatusCode);
    }
}
