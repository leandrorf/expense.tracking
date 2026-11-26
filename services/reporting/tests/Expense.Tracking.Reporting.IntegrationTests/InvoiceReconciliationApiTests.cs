using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Reporting.IntegrationTests;

/// <summary>
/// RCN-2 (ADR-0011, decisoes 7 e 8): conferir a fatura com a do banco pelo total. Hoje = 2026-11-21; o cartao
/// fecha dia 10 e vence dia 17 (fatura atual = 2026-12).
/// </summary>
public sealed class InvoiceReconciliationApiTests(InvoicesApiFactory factory) : IClassFixture<InvoicesApiFactory>
{
    private readonly InvoicesApiFactory _factory = factory;

    private HttpClient ClientFor(Guid owner)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, owner.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, "expenses.read expenses.write");
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(HttpClient Client, Guid CardId)> CardWithDecemberInvoiceAsync()
    {
        var owner = Guid.NewGuid();
        var card = _factory.Cards.Add(owner, closingDay: 10, dueDay: 17);
        _factory.EventFeed.CardPurchase(Guid.NewGuid(), owner, card.Id, 3000m, "2026-11-12", description: "Notebook");
        _factory.EventFeed.CardPurchase(Guid.NewGuid(), owner, card.Id, 180m, "2026-11-20", description: "Mercado");
        await _factory.DrainFeedAsync();
        return (ClientFor(owner), card.Id);
    }

    [Fact]
    public async Task Reconcile_ByTheBankTotal_KeepsTheAcceptedDifference_AndShowsItOnTheInvoice()
    {
        var (client, cardId) = await CardWithDecemberInvoiceAsync();
        var url = $"/reports/cards/{cardId}/invoices/2026-12/reconciliation";

        var before = await client.GetFromJsonAsync<JsonElement>(url);
        Assert.Equal((3180m, JsonValueKind.Null, JsonValueKind.Null), (before.GetProperty("appTotal").GetDecimal(),
            before.GetProperty("bankTotal").ValueKind, before.GetProperty("reconciliation").ValueKind));

        var put = await client.PutAsJsonAsync(url, new { bankTotal = 3214.50m });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var reconciled = await Json(put);
        Assert.Equal((3214.50m, 34.50m), (reconciled.GetProperty("bankTotal").GetDecimal(), reconciled.GetProperty("difference").GetDecimal()));
        var stored = reconciled.GetProperty("reconciliation");
        Assert.Equal((3214.50m, 3180m, 34.50m, InvoicesApiFactory.Now), (stored.GetProperty("bankTotal").GetDecimal(),
            stored.GetProperty("appTotal").GetDecimal(), stored.GetProperty("difference").GetDecimal(),
            stored.GetProperty("reconciledAtUtc").GetDateTimeOffset()));

        var invoices = (await client.GetFromJsonAsync<JsonElement>($"/reports/cards/{cardId}/invoices")).GetProperty("invoices").EnumerateArray().ToList();
        Assert.Equal(34.50m, invoices.Single(i => i.GetProperty("dueMonth").GetString() == "2026-12")
            .GetProperty("reconciliation").GetProperty("difference").GetDecimal());
        Assert.All(invoices.Where(i => i.GetProperty("dueMonth").GetString() != "2026-12"),
            i => Assert.Equal(JsonValueKind.Null, i.GetProperty("reconciliation").ValueKind));

        // Conferir de novo substitui; desfazer apaga (e desfazer o que nao existe = 404).
        Assert.Equal(0m, (await Json(await client.PutAsJsonAsync(url, new { bankTotal = 3180m }))).GetProperty("difference").GetDecimal());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(url)).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await client.GetFromJsonAsync<JsonElement>(url)).GetProperty("reconciliation").ValueKind);
    }

    [Fact]
    public async Task Reconcile_RefusesInvalidTotalsMonthsAndCardsOfOthers()
    {
        var (client, cardId) = await CardWithDecemberInvoiceAsync();
        var url = $"/reports/cards/{cardId}/invoices/2026-12/reconciliation";

        async Task<(HttpStatusCode, string?)> ProblemOf(HttpResponseMessage r) =>
            (r.StatusCode, (await Json(r)).GetProperty("title").GetString());

        Assert.Equal((HttpStatusCode.BadRequest, "reconciliation.bank_total_invalid"), await ProblemOf(await client.PutAsJsonAsync(url, new { bankTotal = 10.123m })));
        Assert.Equal((HttpStatusCode.BadRequest, "reconciliation.bank_total_invalid"), await ProblemOf(await client.PutAsJsonAsync(url, new { })));
        Assert.Equal((HttpStatusCode.BadRequest, "invoice.month_invalid"),
            await ProblemOf(await client.GetAsync($"/reports/cards/{cardId}/invoices/dezembro/reconciliation")));
        Assert.Equal((HttpStatusCode.NotFound, "card.not_found"),
            await ProblemOf(await ClientFor(Guid.NewGuid()).PutAsJsonAsync(url, new { bankTotal = 10m })));

        // Fatura sem compra: total do app 0.
        Assert.Equal(0m, (await client.GetFromJsonAsync<JsonElement>($"/reports/cards/{cardId}/invoices/2027-05/reconciliation"))
            .GetProperty("appTotal").GetDecimal());
    }
}
