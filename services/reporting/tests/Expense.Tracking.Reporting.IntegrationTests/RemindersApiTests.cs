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
