using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Expense.Tracking.Transactions.Application.Abstractions;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// CTB-5 (ADR-0006, decisao 7): plano de contas em <c>/transactions/ledger-accounts</c>. O codigo
/// sai na coluna "Conta contabil" da exportacao; categoria sem conta sai vazia, sem bloquear.
/// </summary>
public sealed class LedgerAccountsApiTests(TransactionsApiFactory factory) : IClassFixture<TransactionsApiFactory>
{
    private const string Route = "/transactions/ledger-accounts";
    private readonly TransactionsApiFactory _factory = factory;

    private HttpClient ClientFor(Guid owner, string? scope = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, owner.ToString());
        if (scope is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, scope);
        }

        return client;
    }

    private static async Task PostAsync(HttpClient client, Guid accountId, string category, decimal amount) =>
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/transactions", new
        {
            accountId, type = "Expense", paymentMethod = "Pix", description = $"Gasto {category}", category, amount,
            currency = "BRL", occurredOn = "2026-10-05",
        })).StatusCode);

    private static async Task<string?> TitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString();

    private static async Task<Dictionary<string, string[]>> ExportAsync(HttpClient client)
    {
        var response = await client.GetAsync("/transactions/accounting-export?month=2026-10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync());
        var files = new Dictionary<string, string[]>();
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            files[entry.FullName] = (await reader.ReadToEndAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        }

        return files;
    }

    [Fact]
    public async Task CategoriesOfBusinessAccounts_GetALedgerCode_ThatGoesToTheExport()
    {
        var (owner, business, personal) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _factory.AccountsClient.Accounts.Clear();
        _factory.AccountsClient.Accounts.AddRange(
        [
            new AccountInfo(business, "Empresa", "Business", "12345678000195", "BRL", true),
            new AccountInfo(personal, "Pessoal", "Personal", null, "BRL", true),
        ]);
        var client = ClientFor(owner);
        await PostAsync(client, business, "Software", 100m);
        await PostAsync(client, business, "Software", 50m);
        await PostAsync(client, business, "software", 10m);
        await PostAsync(client, business, "Viagem", 300m);
        await PostAsync(client, personal, "Mercado", 75m);

        // Categorias PJ como foram digitadas ("Software" e "software" separadas); a PF nao aparece.
        var listed = await client.GetFromJsonAsync<JsonElement[]>(Route);
        Assert.Equal([("Software", 2), ("Viagem", 1), ("software", 1)],
            listed!.Select(l => (l.GetProperty("category").GetString(), l.GetProperty("lines").GetInt32())));
        Assert.All(listed!, l => Assert.Equal(JsonValueKind.Null, l.GetProperty("code").ValueKind));

        var set = await client.PutAsJsonAsync(Route, new { category = "Software", code = "3.1.01", name = "Software" });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var changed = await client.PutAsJsonAsync(Route, new { category = " Software ", code = "3.1.02", name = "Despesas com software" });
        Assert.Equal("3.1.02", (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var invalid = await client.PutAsJsonAsync(Route, new { category = "Viagem", code = " " });
        Assert.Equal((HttpStatusCode.BadRequest, "ledger_account.code_required"), (invalid.StatusCode, await TitleAsync(invalid)));

        var software = (await client.GetFromJsonAsync<JsonElement[]>(Route))!.First(l => l.GetProperty("category").GetString() == "Software");
        Assert.Equal(("3.1.02", "Despesas com software"), (software.GetProperty("code").GetString(), software.GetProperty("name").GetString()));

        // Exportacao: so "Software" sai com o codigo; "software" e "Viagem" saem vazias.
        var files = await ExportAsync(client);
        Assert.Equal(["Software;3.1.02", "Software;3.1.02", "Viagem;", "software;"],
            files["lancamentos.csv"].Skip(1).Select(l => string.Join(';', l.Split(';')[2..4])).Order(StringComparer.Ordinal));
        Assert.Contains("BRL;Software;3.1.02;0,00;150,00;-150,00", files["resumo.csv"]);
        Assert.Contains("BRL;software;;0,00;10,00;-10,00", files["resumo.csv"]);

        var preview = await client.GetFromJsonAsync<JsonElement>("/transactions/accounting-export/preview?month=2026-10");
        Assert.Equal((4, 2), (preview.GetProperty("lines").GetInt32(), preview.GetProperty("unmappedLines").GetInt32()));

        // Tirar a associacao: a coluna volta a sair vazia; tirar de novo e 404.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Route}?category=Software")).StatusCode);
        var again = await client.DeleteAsync($"{Route}?category=Software");
        Assert.Equal((HttpStatusCode.NotFound, "ledger_account.not_found"), (again.StatusCode, await TitleAsync(again)));
        var cleared = await client.GetFromJsonAsync<JsonElement>("/transactions/accounting-export/preview?month=2026-10");
        Assert.Equal(4, cleared.GetProperty("unmappedLines").GetInt32());
    }

    [Fact]
    public async Task MappingNeedsTheWriteScope_AndIsPerUser()
    {
        var reader = ClientFor(Guid.NewGuid(), scope: "expenses.read");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await reader.PutAsJsonAsync(Route, new { category = "Software", code = "1" })).StatusCode);

        var (owner, other) = (ClientFor(Guid.NewGuid()), ClientFor(Guid.NewGuid()));
        _factory.AccountsClient.Accounts.Clear();
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync(Route, new { category = "Software", code = "1" })).StatusCode);

        // Categoria associada sem lancamento PJ continua na lista (com 0), para poder sair.
        var mine = Assert.Single((await owner.GetFromJsonAsync<JsonElement[]>(Route))!);
        Assert.Equal(("Software", "1", 0), (mine.GetProperty("category").GetString(), mine.GetProperty("code").GetString(),
            mine.GetProperty("lines").GetInt32()));
        Assert.Empty((await other.GetFromJsonAsync<JsonElement[]>(Route))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"{Route}?category=Software")).StatusCode);
    }
}
