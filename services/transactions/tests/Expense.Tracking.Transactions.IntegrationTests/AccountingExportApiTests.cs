using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Expense.Tracking.Transactions.Application.Abstractions;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// CTB-2 e CTB-3 (ADR-0006): <c>GET /transactions/accounting-export</c> devolve um ZIP com
/// lancamentos, pagamentos de fatura e resumo do mes, so com contas PJ.
/// </summary>
public sealed class AccountingExportApiTests(TransactionsApiFactory factory) : IClassFixture<TransactionsApiFactory>
{
    private const string Route = "/transactions/accounting-export";
    private const string Cnpj = "12345678000195";
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

    /// <summary>O fake devolve as mesmas contas a qualquer usuario: cada teste monta as suas.</summary>
    private (AccountInfo Business, AccountInfo Personal) GivenAccounts()
    {
        // Conta PJ inativa: continua entrando na exportacao (ADR-0006, decisao 5).
        var business = new AccountInfo(Guid.NewGuid(), "Empresa", "Business", Cnpj, "BRL", IsActive: false);
        var personal = new AccountInfo(Guid.NewGuid(), "Pessoal", "Personal", null, "BRL", IsActive: true);
        _factory.AccountsClient.Accounts.Clear();
        _factory.AccountsClient.Accounts.AddRange([business, personal]);
        return (business, personal);
    }

    private static async Task PostAsync(HttpClient client, object transaction) =>
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/transactions", transaction)).StatusCode);

    private static object Tx(Guid accountId, string description, decimal amount, string occurredOn,
        string type = "Expense", string method = "Pix", Guid? cardId = null, int installments = 1, string? notes = null) => new
    {
        accountId,
        type,
        paymentMethod = method,
        creditCardId = cardId,
        description,
        category = "Geral",
        amount,
        currency = "BRL",
        occurredOn,
        notes,
        installments,
    };

    private static async Task<string[]> CsvLinesAsync(HttpResponseMessage response, string file = "lancamentos.csv")
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync());
        Assert.Equal(["lancamentos.csv", "pagamentos-fatura.csv", "resumo.csv"], zip.Entries.Select(e => e.FullName));
        var entry = zip.GetEntry(file)!;
        using var reader = new StreamReader(entry.Open(), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
        var text = await reader.ReadToEndAsync();
        Assert.StartsWith("﻿", text);
        return text[1..].Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task<string?> TitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString();

    [Fact]
    public async Task Export_HasOnlyTheBusinessAccountsLinesOfTheMonth_WithTheInstallmentPurchaseOnce()
    {
        var owner = Guid.NewGuid();
        var (business, personal) = GivenAccounts();
        var card = Guid.NewGuid();
        _factory.AccountsClient.Cards.Add(new CardInfo(card, business.Id, "Corporativo", "4321"));
        var client = ClientFor(owner);

        await PostAsync(client, Tx(business.Id, "Cliente A", 5000m, "2026-10-20", type: "Income", notes: "NF 12"));
        await PostAsync(client, Tx(business.Id, "Notebook", 3000m, "2026-10-03", method: "CreditCard", cardId: card, installments: 3));
        await PostAsync(client, Tx(business.Id, "=cmd|' /C calc'!A0", 10m, "2026-10-05"));
        await PostAsync(client, Tx(business.Id, "Mes seguinte", 1m, "2026-11-01"));
        await PostAsync(client, Tx(personal.Id, "Mercado pessoal", 200m, "2026-10-04"));

        var response = await client.GetAsync($"{Route}?month=2026-10");

        Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.Equal($"contabil-{Cnpj}-2026-10.zip", disposition.FileNameStar ?? disposition.FileName);
        Assert.Equal(
        [
            "Data;Descrição;Categoria;Conta contábil;Tipo;Valor;Moeda;Meio;Cartão;Parcelas;Conta;CNPJ;Observações;Origem",
            "03/10/2026;Notebook;Geral;;Despesa;-3000,00;BRL;Cartão de crédito;Corporativo final 4321;3;Empresa;12.345.678/0001-95;;digitado",
            "05/10/2026;'=cmd|' /C calc'!A0;Geral;;Despesa;-10,00;BRL;Pix;;1;Empresa;12.345.678/0001-95;;digitado",
            "20/10/2026;Cliente A;Geral;;Receita;5000,00;BRL;Pix;;1;Empresa;12.345.678/0001-95;NF 12;digitado",
        ], await CsvLinesAsync(response));
    }

    [Fact]
    public async Task Package_HasThePaymentsWithABusinessSide_AndTheSummaryByCurrencyAndCategory()
    {
        var owner = Guid.NewGuid();
        var (business, personal) = GivenAccounts();
        var (businessCard, personalCard) = (Guid.NewGuid(), Guid.NewGuid());
        _factory.AccountsClient.Cards.AddRange(
        [
            new CardInfo(businessCard, business.Id, "Corporativo", "4321"),
            new CardInfo(personalCard, personal.Id, "Pessoal", "1111"),
        ]);
        var client = ClientFor(owner);

        await PostAsync(client, Tx(business.Id, "Cliente A", 5000m, "2026-10-20", type: "Income"));
        await PostAsync(client, Tx(business.Id, "Licenca", 300m, "2026-10-02"));
        await PostAsync(client, new
        {
            accountId = business.Id, type = "Expense", paymentMethod = "Pix", description = "Nuvem", category = "Software",
            amount = 120.5m, currency = "BRL", occurredOn = "2026-10-09",
        });
        await PostAsync(client, new
        {
            accountId = business.Id, type = "Expense", paymentMethod = "Pix", description = "SaaS", category = "Software",
            amount = 10m, currency = "USD", occurredOn = "2026-10-10",
        });

        async Task Pay(Guid card, Guid from, decimal amount, string paidOn) => Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/transactions/card-payments", new
            {
                creditCardId = card, invoiceDueMonth = "2026-10", accountId = from, amount, paidOn,
            })).StatusCode);
        await Pay(businessCard, personal.Id, 800m, "2026-10-17");   // cartao PJ pago pela conta PF: entra
        await Pay(personalCard, business.Id, 150m, "2026-10-12");   // cartao PF pago pela conta PJ: entra
        await Pay(personalCard, personal.Id, 999m, "2026-10-17");   // so PF: fica de fora
        await Pay(businessCard, business.Id, 70m, "2026-11-01");    // outro mes: fica de fora

        var response = await client.GetAsync($"{Route}?month=2026-10");

        Assert.Equal(
        [
            "Data;Cartão;Fatura;Conta de origem;CNPJ;Valor;Moeda",
            "12/10/2026;Pessoal final 1111;10/2026;Empresa;12.345.678/0001-95;150,00;BRL",
            "17/10/2026;Corporativo final 4321;10/2026;Pessoal;;800,00;BRL",
        ], await CsvLinesAsync(response, "pagamentos-fatura.csv"));
        Assert.Equal(
        [
            "Moeda;Categoria;Conta contábil;Entradas;Saídas;Saldo",
            "BRL;Geral;;5000,00;300,00;4700,00",
            "BRL;Software;;0,00;120,50;-120,50",
            "BRL;Total;;5000,00;420,50;4579,50",
            "USD;Software;;0,00;10,00;-10,00",
            "USD;Total;;0,00;10,00;-10,00",
        ], await CsvLinesAsync(await client.GetAsync($"{Route}?month=2026-10"), "resumo.csv"));
    }

    [Fact]
    public async Task Preview_CountsAndTotalsTheSamePackage_WithoutTheFile()
    {
        var owner = Guid.NewGuid();
        var (business, personal) = GivenAccounts();
        var client = ClientFor(owner, scope: "expenses.read expenses.write");
        await PostAsync(client, Tx(business.Id, "Cliente A", 5000m, "2026-10-20", type: "Income"));
        await PostAsync(client, Tx(business.Id, "Licenca", 300.25m, "2026-10-02"));
        await PostAsync(client, Tx(personal.Id, "Mercado pessoal", 200m, "2026-10-04"));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/transactions/card-payments", new
        {
            creditCardId = Guid.NewGuid(), invoiceDueMonth = "2026-10", accountId = business.Id, amount = 80m, paidOn = "2026-10-15",
        })).StatusCode);

        var preview = await client.GetFromJsonAsync<JsonElement>($"{Route}/preview?month=2026-10");

        Assert.Equal(($"contabil-{Cnpj}-2026-10.zip", 1, 2, 1), (preview.GetProperty("fileName").GetString(),
            preview.GetProperty("accounts").GetInt32(), preview.GetProperty("lines").GetInt32(), preview.GetProperty("payments").GetInt32()));
        var brl = Assert.Single(preview.GetProperty("totals").EnumerateArray());
        Assert.Equal(2, preview.GetProperty("unmappedLines").GetInt32());
        Assert.Equal(("BRL", 5000m, 300.25m, 4699.75m), (brl.GetProperty("currency").GetString(),
            brl.GetProperty("income").GetDecimal(), brl.GetProperty("expense").GetDecimal(), brl.GetProperty("balance").GetDecimal()));

        var pf = await client.GetAsync($"{Route}/preview?month=2026-10&accountId={personal.Id}");
        Assert.Equal((HttpStatusCode.BadRequest, "accounting_export.account_not_business"), (pf.StatusCode, await TitleAsync(pf)));
        var bad = await client.GetAsync($"{Route}/preview?month=10-2026");
        Assert.Equal("accounting_export.month_invalid", await TitleAsync(bad));
    }

    [Fact]
    public async Task Export_OfOneAccount_RejectsPersonalOrForeignAccounts()
    {
        var owner = Guid.NewGuid();
        var (business, personal) = GivenAccounts();
        var client = ClientFor(owner);
        await PostAsync(client, Tx(business.Id, "Software", 99m, "2026-10-08"));

        var one = await client.GetAsync($"{Route}?month=2026-10&accountId={business.Id}");
        Assert.Equal(2, (await CsvLinesAsync(one)).Length);

        var pf = await client.GetAsync($"{Route}?month=2026-10&accountId={personal.Id}");
        Assert.Equal((HttpStatusCode.BadRequest, "accounting_export.account_not_business"), (pf.StatusCode, await TitleAsync(pf)));

        var foreign = await client.GetAsync($"{Route}?month=2026-10&accountId={Guid.NewGuid()}");
        Assert.Equal((HttpStatusCode.NotFound, "account.not_found"), (foreign.StatusCode, await TitleAsync(foreign)));
    }

    [Fact]
    public async Task Export_WithoutBusinessAccount_OrWithABadMonth_OrWithoutToken_Fails()
    {
        _factory.AccountsClient.Accounts.Clear();
        _factory.AccountsClient.Accounts.Add(new AccountInfo(Guid.NewGuid(), "Pessoal", "Personal", null, "BRL", true));
        var client = ClientFor(Guid.NewGuid(), scope: "expenses.read");

        var none = await client.GetAsync($"{Route}?month=2026-10");
        Assert.Equal((HttpStatusCode.BadRequest, "accounting_export.no_business_account"), (none.StatusCode, await TitleAsync(none)));

        foreach (var month in new[] { "", "2026-13", "10/2026", "2026-10-01" })
        {
            var bad = await client.GetAsync($"{Route}?month={month}");
            Assert.Equal((HttpStatusCode.BadRequest, "accounting_export.month_invalid"), (bad.StatusCode, await TitleAsync(bad)));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync($"{Route}?month=2026-10")).StatusCode);
    }
}
