using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Expense.Tracking.Transactions.Domain.Imports;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// INV-11 (ADR-0005, decisao 10): no extrato de conta importado, a saida "PAG FATURA" vira pagamento
/// de fatura (nunca gasto), e reimportar ou desfazer trata o pagamento como trata os lancamentos.
/// </summary>
public sealed class ImportInvoicePaymentApiTests(ImportsApiTests.Factory factory) : IClassFixture<ImportsApiTests.Factory>
{
    private const string Mapping = """{"dateColumn":0,"descriptionColumn":1,"amountColumn":2,"hasHeader":true,"decimalSeparator":","}""";

    private static readonly byte[] Statement = Encoding.UTF8.GetBytes(
        "Data;Descricao;Valor\n16/11/2026;PAG FATURA CARTAO;-1.500,00\n16/11/2026;PADARIA;-30,00\n");

    private readonly ImportsApiTests.Factory _factory = factory;

    private HttpClient ClientFor(Guid owner)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, owner.ToString());
        return client;
    }

    private static MultipartFormDataContent Upload(Guid accountId)
    {
        var file = new ByteArrayContent(Statement);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent
        {
            { file, "file", "extrato.csv" },
            { new StringContent(accountId.ToString()), "accountId" },
            { new StringContent("Pix"), "paymentMethod" },
            { new StringContent(Mapping), "mapping" },
        };
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static JsonElement[] Lines(JsonElement batch) => batch.GetProperty("lines").EnumerateArray().ToArray();

    [Fact]
    public async Task InvoicePaymentLine_BecomesACardPayment_IsDeduplicated_AndUndone()
    {
        var (owner, accountId, card) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _factory.AccountsClient.AccountCards[accountId] = [new InvoiceCardHint(card, DueDay: 17)];
        var client = ClientFor(owner);

        var preview = await Json(await client.PostAsync("/imports", Upload(accountId)));
        var (payment, bakery) = (Lines(preview)[0], Lines(preview)[1]);
        Assert.Equal((true, card, "2026-11"), (payment.GetProperty("invoicePayment").GetBoolean(),
            payment.GetProperty("invoiceCardId").GetGuid(), payment.GetProperty("invoiceDueMonth").GetString()));
        Assert.False(bakery.GetProperty("invoicePayment").GetBoolean());

        // So a padaria precisa de categoria; o pagamento de fatura nao e gasto.
        var confirm = await client.PostAsJsonAsync($"/imports/{preview.GetProperty("id").GetGuid()}/confirm", new
        {
            lines = new[] { new { id = bakery.GetProperty("id").GetGuid(), category = "Padaria" } },
        });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var confirmed = await Json(confirm);
        Assert.Equal(2, confirmed.GetProperty("summary").GetProperty("imported").GetInt32());
        var paymentId = Lines(confirmed)[0].GetProperty("cardPaymentId").GetGuid();

        var transactions = await client.GetFromJsonAsync<JsonElement[]>($"/transactions?accountId={accountId}");
        Assert.Equal("PADARIA", Assert.Single(transactions!).GetProperty("description").GetString());
        var paid = Assert.Single((await client.GetFromJsonAsync<JsonElement[]>($"/transactions/card-payments?creditCardId={card}"))!);
        Assert.Equal((paymentId, 1500m, "2026-11", "2026-11-16", accountId), (paid.GetProperty("id").GetGuid(),
            paid.GetProperty("amount").GetDecimal(), paid.GetProperty("invoiceDueMonth").GetString(),
            paid.GetProperty("paidOn").GetString(), paid.GetProperty("accountId").GetGuid()));

        // Reimportar: o pagamento ja feito tambem e duplicata (nao paga a fatura de novo).
        var again = await Json(await client.PostAsync("/imports", Upload(accountId)));
        Assert.All(Lines(again), l => Assert.Equal("Duplicate", l.GetProperty("status").GetString()));

        // Desfazer: o pagamento sai junto com o lancamento.
        var undo = await client.PostAsync($"/imports/{confirmed.GetProperty("id").GetGuid()}/undo", null);
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>($"/transactions/card-payments?creditCardId={card}"))!);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>($"/transactions?accountId={accountId}"))!);
    }

    [Fact]
    public async Task UserCanSwitchTheLineToAnExpense_OrPickAnotherCard_ButNotSomeoneElsesCard()
    {
        var (owner, accountId, foreignCard) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _factory.AccountsClient.InvalidCards.Add(foreignCard);
        var client = ClientFor(owner);

        // Sem cartao na conta: vem sugerida como pagamento, mas sem cartao; confirmar assim pede a fatura.
        var preview = await Json(await client.PostAsync("/imports", Upload(accountId)));
        var batchId = preview.GetProperty("id").GetGuid();
        var (payment, bakery) = (Lines(preview)[0], Lines(preview)[1]);
        Assert.True(payment.GetProperty("invoicePayment").GetBoolean());
        Assert.Equal(JsonValueKind.Null, payment.GetProperty("invoiceCardId").ValueKind);
        var bakeryDecision = new { id = bakery.GetProperty("id").GetGuid(), category = "Padaria" };

        var missing = await client.PostAsJsonAsync($"/imports/{batchId}/confirm", new { lines = new object[] { bakeryDecision } });
        Assert.Equal("import.invoice_required", (await Json(missing)).GetProperty("title").GetString());

        var foreign = await client.PostAsJsonAsync($"/imports/{batchId}/confirm", new
        {
            lines = new object[]
            {
                bakeryDecision,
                new { id = payment.GetProperty("id").GetGuid(), invoiceCardId = foreignCard, invoiceDueMonth = "2026-11" },
            },
        });
        Assert.Equal((HttpStatusCode.NotFound, "card.not_found"), (foreign.StatusCode, (await Json(foreign)).GetProperty("title").GetString()));

        var badMonth = await client.PostAsJsonAsync($"/imports/{batchId}/confirm", new
        {
            lines = new object[] { new { id = payment.GetProperty("id").GetGuid(), invoiceDueMonth = "11/2026" } },
        });
        Assert.Equal("import.invoice_month_invalid", (await Json(badMonth)).GetProperty("title").GetString());

        // Lancamento comum: vira gasto com categoria, como antes da INV-11.
        var asExpense = await client.PostAsJsonAsync($"/imports/{batchId}/confirm", new
        {
            lines = new object[]
            {
                bakeryDecision,
                new { id = payment.GetProperty("id").GetGuid(), invoicePayment = false, category = "Cartao" },
            },
        });
        Assert.Equal(HttpStatusCode.OK, asExpense.StatusCode);
        var transactions = await client.GetFromJsonAsync<JsonElement[]>($"/transactions?accountId={accountId}");
        Assert.Equal(["Cartao", "Padaria"], transactions!.Select(t => t.GetProperty("category").GetString()).Order());
    }
}
