using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Expense.Tracking.Transactions.Application.Abstractions;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// CTB-2 (ADR-0006): <c>GET /transactions/accounting-export</c> devolve um ZIP com o
/// <c>lancamentos.csv</c> do mes, so com contas PJ.
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

    private static async Task<string[]> CsvLinesAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync());
        var entry = Assert.Single(zip.Entries);
        Assert.Equal("lancamentos.csv", entry.FullName);
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
