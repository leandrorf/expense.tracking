using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// REC-9 (ADR-0008, decisao 11): a previa do extrato sugere a ocorrencia em aberto de uma regra da conta;
/// confirmar fecha o periodo com o lancamento importado; trocar para comum nao vincula; desfazer reabre.
/// </summary>
public sealed class RecurrenceImportApiTests(TransactionsApiFactory factory) : IClassFixture<TransactionsApiFactory>
{
    private readonly TransactionsApiFactory _factory = factory;

    private HttpClient ClientFor(Guid owner)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, owner.ToString());
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Extrato OFX de conta com (data yyyyMMdd, valor com sinal, memo).</summary>
    private static MultipartFormDataContent Statement(Guid accountId, params (string Date, string Amount, string Memo)[] lines)
    {
        var ofx = string.Join("\r\n", [
            "OFXHEADER:100", "DATA:OFXSGML", "VERSION:102", "CHARSET:1252", "",
            "<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><CURDEF>BRL<BANKTRANLIST>",
            .. lines.Select((l, i) => $"<STMTTRN><DTPOSTED>{l.Date}<TRNAMT>{l.Amount}<FITID>REC-{Guid.NewGuid():N}-{i}<MEMO>{l.Memo}</STMTTRN>"),
            "</BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>",
        ]);
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.Latin1.GetBytes(ofx));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "extrato.ofx");
        form.Add(new StringContent(accountId.ToString()), "accountId");
        form.Add(new StringContent("Pix"), "paymentMethod");
        return form;
    }

    private static async Task<Guid> RuleAsync(HttpClient client, Guid accountId, string type, decimal amount, string startsOn, string description)
    {
        var created = await client.PostAsJsonAsync("/transactions/recurrences",
            RecurrencesApiTests.Rule(accountId, startsOn: startsOn, amount: amount, type: type, description: description,
                method: type == "Income" ? "Pix" : "Boleto"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await Json(created)).GetProperty("id").GetGuid();
    }

    private static async Task<string?> NextPeriodAsync(HttpClient client, Guid ruleId) =>
        (await client.GetFromJsonAsync<JsonElement>($"/transactions/recurrences/{ruleId}")).GetProperty("nextOccurrence").GetProperty("period").GetString();

    [Fact]
    public async Task Preview_SuggestsTheOpenOccurrence_ConfirmClosesIt_SwitchingToCommonDoesNot_AndUndoReopens()
    {
        var (client, accountId) = (ClientFor(Guid.NewGuid()), Guid.NewGuid());
        var rent = await RuleAsync(client, accountId, "Expense", 2000m, "2030-01-10", "Aluguel");
        var salary = await RuleAsync(client, accountId, "Income", 5000m, "2030-01-18", "Salario");
        // Regra de outra conta nunca casa.
        var elsewhere = await RuleAsync(client, Guid.NewGuid(), "Expense", 300m, "2030-01-15", "Escola");

        var preview = await Json(await client.PostAsync("/imports", Statement(accountId,
            ("20300111", "-2050.00", "BOLETO ALUGUEL"),
            ("20300112", "-2000.00", "OUTRO BOLETO"),
            ("20300120", "5000.00", "TED SALARIO"),
            ("20300115", "-300.00", "ESCOLA"))));

        var lines = preview.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal((rent, "2030-01", "Aluguel", "Moradia"), (lines[0].GetProperty("recurrenceId").GetGuid(),
            lines[0].GetProperty("recurrencePeriod").GetString(), lines[0].GetProperty("recurrenceDescription").GetString(),
            lines[0].GetProperty("category").GetString()));
        Assert.Equal(JsonValueKind.Null, lines[1].GetProperty("recurrenceId").ValueKind);
        Assert.Equal(salary, lines[2].GetProperty("recurrenceId").GetGuid());
        Assert.Equal(JsonValueKind.Null, lines[3].GetProperty("recurrenceId").ValueKind);

        // O salario vira lancamento comum; o aluguel fica como ocorrencia.
        var batchId = preview.GetProperty("id").GetGuid();
        var confirmed = await Json(await client.PostAsJsonAsync($"/imports/{batchId}/confirm", new
        {
            lines = lines.Select(l => new
            {
                id = l.GetProperty("id").GetGuid(),
                included = true,
                category = l.GetProperty("category").ValueKind == JsonValueKind.Null ? "Importados" : null,
                recurrence = l.GetProperty("recurrenceId").ValueKind == JsonValueKind.Null || l.GetProperty("recurrenceId").GetGuid() != salary
                    ? (bool?)null
                    : false,
            }).ToArray(),
        }));

        var rentTx = confirmed.GetProperty("lines")[0].GetProperty("transactionId").GetGuid();
        var salaryTx = confirmed.GetProperty("lines")[2].GetProperty("transactionId").GetGuid();
        var rentRead = await client.GetFromJsonAsync<JsonElement>($"/transactions/{rentTx}");
        Assert.Equal((rent, "2030-01", 2050m, true), (rentRead.GetProperty("recurrenceId").GetGuid(),
            rentRead.GetProperty("recurrencePeriod").GetString(), rentRead.GetProperty("amount").GetDecimal(), rentRead.GetProperty("imported").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, (await client.GetFromJsonAsync<JsonElement>($"/transactions/{salaryTx}")).GetProperty("recurrenceId").ValueKind);
        Assert.Equal("2030-02", await NextPeriodAsync(client, rent));
        Assert.Equal("2030-01", await NextPeriodAsync(client, salary));
        Assert.Equal("2030-01", await NextPeriodAsync(client, elsewhere));

        // Desfazer a importacao apaga o lancamento e reabre o periodo.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/imports/{batchId}/undo", null)).StatusCode);
        Assert.Equal("2030-01", await NextPeriodAsync(client, rent));
    }

    [Fact]
    public async Task Confirm_WhenThePeriodWasClosedAfterThePreview_ImportsACommonTransaction()
    {
        var (client, accountId) = (ClientFor(Guid.NewGuid()), Guid.NewGuid());
        var rent = await RuleAsync(client, accountId, "Expense", 2000m, "2030-01-10", "Aluguel");
        var preview = await Json(await client.PostAsync("/imports", Statement(accountId, ("20300110", "-2000.00", "BOLETO ALUGUEL"))));
        Assert.Equal(rent, preview.GetProperty("lines")[0].GetProperty("recurrenceId").GetGuid());

        // Outra aba pulou o periodo entre a previa e a confirmacao.
        await client.PostAsync($"/transactions/recurrences/{rent}/occurrences/2030-01/skip", null);
        var confirmed = await Json(await client.PostAsJsonAsync($"/imports/{preview.GetProperty("id").GetGuid()}/confirm", new { lines = Array.Empty<object>() }));

        var tx = await client.GetFromJsonAsync<JsonElement>($"/transactions/{confirmed.GetProperty("lines")[0].GetProperty("transactionId").GetGuid()}");
        Assert.Equal(JsonValueKind.Null, tx.GetProperty("recurrenceId").ValueKind);
        Assert.Equal("2030-02", await NextPeriodAsync(client, rent));
    }
}
