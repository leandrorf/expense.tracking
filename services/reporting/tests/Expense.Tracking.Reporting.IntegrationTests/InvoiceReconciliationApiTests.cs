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

    // ---------- RCN-4 (ADR-0011, decisoes 1 a 6): com o arquivo ----------

    private static Expense.Tracking.Reporting.Application.Invoices.ImportLineSnapshot BankLine(
        int number, string date, decimal amount, string description, string type = "Expense", Guid? duplicateOf = null,
        string status = "New") =>
        new(Guid.NewGuid(), number, status, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), type, amount,
            description, null, duplicateOf);

    [Fact]
    public async Task Reconcile_WithTheBankFile_GroupsEveryLineAndItem_AndExplainsTheDifference()
    {
        // Fatura 2026-12 do app: notebook 3x de 1000 (parcela 1), mercado 180 (lancado a mao), padaria 25 (a mais).
        var owner = Guid.NewGuid();
        var card = _factory.Cards.Add(owner, closingDay: 10, dueDay: 17);
        var (notebook, market, bakery) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _factory.EventFeed.CardPurchase(notebook, owner, card.Id, 3000m, "2026-11-12", installments: 3, description: "Notebook Loja");
        _factory.EventFeed.CardPurchase(market, owner, card.Id, 180m, "2026-11-20", description: "Mercado");
        _factory.EventFeed.CardPurchase(bakery, owner, card.Id, 25m, "2026-11-21", description: "Padaria");
        await _factory.DrainFeedAsync();

        // Fatura do banco: parcela, mercado (2 dias depois), uma compra que faltou, IOF, o pagamento anterior.
        var batch = _factory.ImportBatches.Add(owner, new(Guid.NewGuid(), "CreditCard", card.Id, 1219.40m, null, new DateOnly(2026, 12, 10),
        [
            BankLine(1, "2026-11-12", 1000m, "LOJA NOTEBOOK PARCELA 01/03"),
            BankLine(2, "2026-11-22", 180m, "SUPERMERCADO BOM PRECO"),
            BankLine(3, "2026-11-25", 39.40m, "FARMACIA"),
            BankLine(4, "2026-11-26", 0.98m, "IOF COMPRA INTERNACIONAL"),
            BankLine(5, "2026-11-18", 900m, "PAGAMENTO RECEBIDO", type: "Income", status: "ExcludedByDefault"),
            BankLine(6, "2026-11-30", 0m, "LINHA INVALIDA", status: "Invalid"),
        ]));

        var body = await ClientFor(owner).GetFromJsonAsync<JsonElement>(
            $"/reports/cards/{card.Id}/invoices/2026-12/reconciliation?batchId={batch.Id}");

        Assert.Equal(1205m, body.GetProperty("appTotal").GetDecimal());
        var detail = body.GetProperty("detail");
        Assert.Equal((1219.40m, 1220.38m, 14.40m, "2026-12"), (detail.GetProperty("fileTotal").GetDecimal(),
            detail.GetProperty("linesTotal").GetDecimal(), detail.GetProperty("difference").GetDecimal(),
            detail.GetProperty("suggestedDueMonth").GetString()));
        var groups = detail.GetProperty("entries").EnumerateArray()
            .Select(e => (e.GetProperty("group").GetString(), e.GetProperty("lineNumber").ValueKind == JsonValueKind.Number ? e.GetProperty("lineNumber").GetInt32() : 0,
                e.GetProperty("transactionId").ValueKind == JsonValueKind.String ? e.GetProperty("transactionId").GetGuid() : Guid.Empty))
            .ToList();
        Assert.Equal(
        [
            ("Matched", 1, notebook), ("Matched", 2, market),
            ("OnlyInBank", 3, Guid.Empty), ("OnlyInBank", 4, Guid.Empty),
            ("OnlyInApp", 0, bakery),
        ], groups);
        var explanation = detail.GetProperty("explanation");
        // 40,38 so no banco - 25 so no app = 15,38; o banco declara 0,98 a menos que a soma das linhas.
        Assert.Equal((40.38m, -25m, 0m, -0.98m), (explanation.GetProperty("onlyInBank").GetDecimal(),
            explanation.GetProperty("onlyInApp").GetDecimal(), explanation.GetProperty("differentAmount").GetDecimal(),
            explanation.GetProperty("unexplained").GetDecimal()));
    }

    [Fact]
    public async Task Reconcile_WithABatchOfAnotherCardOrUser_IsRefused()
    {
        var (client, cardId) = await CardWithDecemberInvoiceAsync();
        var owner = Guid.Parse(client.DefaultRequestHeaders.GetValues(TestAuthHandler.SubHeader).Single());
        var otherCard = _factory.ImportBatches.Add(owner, new(Guid.NewGuid(), "CreditCard", Guid.NewGuid(), null, null, null, []));
        var bankStatement = _factory.ImportBatches.Add(owner, new(Guid.NewGuid(), "BankAccount", null, null, null, null, []));
        var ofAnother = _factory.ImportBatches.Add(Guid.NewGuid(), new(Guid.NewGuid(), "CreditCard", cardId, null, null, null, []));
        var url = $"/reports/cards/{cardId}/invoices/2026-12/reconciliation?batchId=";

        async Task<(HttpStatusCode, string?)> ProblemOf(Guid batchId)
        {
            var r = await client.GetAsync(url + batchId);
            return (r.StatusCode, (await Json(r)).GetProperty("title").GetString());
        }

        Assert.Equal((HttpStatusCode.BadRequest, "reconciliation.batch_card_mismatch"), await ProblemOf(otherCard.Id));
        Assert.Equal((HttpStatusCode.BadRequest, "reconciliation.batch_card_mismatch"), await ProblemOf(bankStatement.Id));
        Assert.Equal((HttpStatusCode.NotFound, "reconciliation.batch_not_found"), await ProblemOf(ofAnother.Id));
    }

    [Fact]
    public async Task Reconciliation_IsMarkedChangedWhenTheInvoiceChangesAfterIt_UntilReconciledAgain()
    {
        // RCN-8: conferida em 3.180; entra uma compra no ciclo; conferir de novo limpa a marca.
        var (client, cardId) = await CardWithDecemberInvoiceAsync();
        var owner = Guid.Parse(client.DefaultRequestHeaders.GetValues(TestAuthHandler.SubHeader).Single());
        var url = $"/reports/cards/{cardId}/invoices/2026-12/reconciliation";
        await client.PutAsJsonAsync(url, new { bankTotal = 3180m });

        async Task<JsonElement> DecemberAsync() =>
            (await client.GetFromJsonAsync<JsonElement>($"/reports/cards/{cardId}/invoices")).GetProperty("invoices").EnumerateArray()
            .Single(i => i.GetProperty("dueMonth").GetString() == "2026-12").GetProperty("reconciliation");

        Assert.False((await DecemberAsync()).GetProperty("changedSince").GetBoolean());

        _factory.EventFeed.CardPurchase(Guid.NewGuid(), owner, cardId, 40m, "2026-11-25", description: "Farmacia");
        await _factory.DrainFeedAsync();

        Assert.True((await DecemberAsync()).GetProperty("changedSince").GetBoolean());
        var view = await client.GetFromJsonAsync<JsonElement>(url);
        Assert.Equal((3220m, -40m, true), (view.GetProperty("appTotal").GetDecimal(), view.GetProperty("difference").GetDecimal(),
            view.GetProperty("reconciliation").GetProperty("changedSince").GetBoolean()));

        await client.PutAsJsonAsync(url, new { bankTotal = 3220m });
        Assert.False((await DecemberAsync()).GetProperty("changedSince").GetBoolean());
    }
}
