using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Reporting.IntegrationTests;

/// <summary>
/// INV-8 (ADR-0005, decisao 8): previsao de caixa pela API. Hoje = 2026-11-21 (relogio parado do
/// <see cref="InvoicesApiFactory"/>), entao a janela padrao vai de 2026-11 a 2027-04.
/// </summary>
public sealed class CashForecastApiTests(InvoicesApiFactory factory) : IClassFixture<InvoicesApiFactory>
{
    private readonly InvoicesApiFactory _factory = factory;

    private HttpClient ClientFor(Guid owner)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, owner.ToString());
        return client;
    }

    private static (decimal Income, decimal Expense, decimal Invoices, decimal Paid, decimal Remaining, decimal Result) Flow(
        JsonElement month, string scope)
    {
        var flow = month.GetProperty(scope);
        return (flow.GetProperty("income").GetDecimal(), flow.GetProperty("expense").GetDecimal(),
            flow.GetProperty("invoices").GetDecimal(), flow.GetProperty("invoicesPaid").GetDecimal(),
            flow.GetProperty("invoicesRemaining").GetDecimal(), flow.GetProperty("result").GetDecimal());
    }

    [Fact]
    public async Task Forecast_PutsEachInstallmentInItsInvoiceMonth_ByTheCardAccountScope()
    {
        var owner = Guid.NewGuid();
        var personal = _factory.Cards.Add(owner, closingDay: 10, dueDay: 17, scope: "Personal");
        var business = _factory.Cards.Add(owner, closingDay: 10, dueDay: 17, scope: "Business");
        var feed = _factory.EventFeed;
        // PF: 10x de 100 a partir da fatura de 2026-12; a compra nao aparece em 2026-11 (data dela).
        feed.CardPurchase(Guid.NewGuid(), owner, personal.Id, 1000m, "2026-11-15", installments: 10);
        // PF: fatura de 2026-11 (venceu 17/11) ja paga; o pagamento nao vira saida de novo.
        feed.CardPurchase(Guid.NewGuid(), owner, personal.Id, 200m, "2026-11-05");
        feed.InvoicePaid(Guid.NewGuid(), owner, personal.Id, "2026-11", 200m, "2026-11-17");
        // PJ: compra no cartao da conta PJ, fatura de 2026-12; o escopo vem da conta do cartao.
        feed.CardPurchase(Guid.NewGuid(), owner, business.Id, 500m, "2026-12-01");
        // Fora do cartao, pela data.
        feed.Registered(Guid.NewGuid(), owner, "Personal", 5000m, "Salario", occurredOn: "2026-12-05", transactionType: "Income");
        feed.Registered(Guid.NewGuid(), owner, "Business", 300m, "Contador", occurredOn: "2026-11-30", paymentMethod: "Boleto");
        feed.Registered(Guid.NewGuid(), owner, "Personal", 999m, "Passado", occurredOn: "2026-10-31");
        // Outro usuario nao entra.
        feed.Registered(Guid.NewGuid(), Guid.NewGuid(), "Personal", 777m, "Outro", occurredOn: "2026-12-05");
        await _factory.DrainFeedAsync();

        var body = await ClientFor(owner).GetFromJsonAsync<JsonElement>("/reports/cash-forecast");

        var months = body.GetProperty("months").EnumerateArray().ToList();
        Assert.Equal(["2026-11", "2026-12", "2027-01", "2027-02", "2027-03", "2027-04"],
            months.Select(m => m.GetProperty("month").GetString()));
        Assert.Contains("já está lançado", body.GetProperty("notice").GetString());

        Assert.Equal((0m, 0m, 200m, 200m, 0m, -200m), Flow(months[0], "personal"));
        Assert.Equal((0m, 300m, 0m, 0m, 0m, -300m), Flow(months[0], "business"));
        Assert.Equal((5000m, 0m, 100m, 0m, 100m, 4900m), Flow(months[1], "personal"));
        Assert.Equal((0m, 0m, 500m, 0m, 500m, -500m), Flow(months[1], "business"));
        Assert.Equal((5000m, 0m, 600m, 0m, 600m, 4400m), Flow(months[1], "total"));
        Assert.All(months.Skip(2), m => Assert.Equal((0m, 0m, 100m, 0m, 100m, -100m), Flow(m, "personal")));
    }

    [Fact]
    public async Task Months_ChangesTheWindow_AndOutOfRangeIsRejected()
    {
        var client = ClientFor(Guid.NewGuid());

        var one = await client.GetFromJsonAsync<JsonElement>("/reports/cash-forecast?months=1");
        var twelve = await client.GetFromJsonAsync<JsonElement>("/reports/cash-forecast?months=12");
        var zero = await client.GetAsync("/reports/cash-forecast?months=0");
        var thirteen = await client.GetAsync("/reports/cash-forecast?months=13");
        var anonymous = await _factory.CreateClient().GetAsync("/reports/cash-forecast");

        Assert.Equal("2026-11", Assert.Single(one.GetProperty("months").EnumerateArray()).GetProperty("month").GetString());
        Assert.Equal("2027-10", twelve.GetProperty("months").EnumerateArray().Last().GetProperty("month").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal("forecast.months_invalid", (await thirteen.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private static decimal? Balance(JsonElement month, string scope)
    {
        var balance = month.GetProperty(scope).GetProperty("balance");
        return balance.ValueKind == JsonValueKind.Null ? null : balance.GetDecimal();
    }

    [Fact]
    public async Task Balance_StartsAtTheInformedBalance_AndFollowsCashPaymentsAndInvoicesOfThatAccount()
    {
        var owner = Guid.NewGuid();
        var pf = _factory.Cards.AddAccount(owner, "Personal", openingBalance: 2000m, openingBalanceOn: "2026-11-10");
        var pj = _factory.Cards.AddAccount(owner, "Business");
        _factory.Cards.AddAccount(owner, "Personal", isActive: false);
        var card = _factory.Cards.Add(owner, closingDay: 10, dueDay: 17, scope: "Personal", accountId: pf.Id);
        var feed = _factory.EventFeed;
        // Fatura de 2026-11 (200), paga em 17/11 pela conta PF: sai na data do pagamento.
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 200m, "2026-11-05");
        feed.InvoicePaid(Guid.NewGuid(), owner, card.Id, "2026-11", 200m, "2026-11-17", accountId: pf.Id);
        // Fatura de 2026-12 (300), ainda a pagar: sai em dezembro.
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 300m, "2026-11-15");
        // Antes do saldo informado (09/11): ja esta nele. Depois: entra.
        feed.Registered(Guid.NewGuid(), owner, "Personal", 100m, "Antes", accountId: pf.Id, occurredOn: "2026-11-09");
        feed.Registered(Guid.NewGuid(), owner, "Personal", 50m, "Depois", accountId: pf.Id, occurredOn: "2026-11-20");
        feed.Registered(Guid.NewGuid(), owner, "Personal", 5000m, "Salario", accountId: pf.Id, occurredOn: "2026-12-05",
            transactionType: "Income");
        // Conta PJ sem saldo: fica fora do saldo projetado (mas continua no fluxo do mes).
        feed.Registered(Guid.NewGuid(), owner, "Business", 300m, "Contador", accountId: pj.Id, occurredOn: "2026-11-30");
        await _factory.DrainFeedAsync();

        var body = await ClientFor(owner).GetFromJsonAsync<JsonElement>("/reports/cash-forecast?months=3");

        var months = body.GetProperty("months").EnumerateArray().ToList();
        Assert.Equal([1750m, 6450m, 6450m], months.Select(m => Balance(m, "personal")));
        Assert.Equal(months.Select(m => Balance(m, "personal")), months.Select(m => Balance(m, "total")));
        Assert.All(months, m => Assert.Null(Balance(m, "business")));
        Assert.Equal(300m, months[0].GetProperty("business").GetProperty("expense").GetDecimal());
        Assert.Equal(1, body.GetProperty("accountsWithoutBalance").GetInt32());
    }

    private static (decimal Income, decimal Expense, decimal Invoices, decimal RecIncome, decimal RecExpense, decimal RecInvoices) Recurring(
        JsonElement month, string scope)
    {
        var flow = month.GetProperty(scope);
        return (flow.GetProperty("income").GetDecimal(), flow.GetProperty("expense").GetDecimal(), flow.GetProperty("invoices").GetDecimal(),
            flow.GetProperty("recurringIncome").GetDecimal(), flow.GetProperty("recurringExpense").GetDecimal(),
            flow.GetProperty("recurringInvoices").GetDecimal());
    }

    [Fact]
    public async Task Recurrences_EnterFromTodayOn_ByDateOrByInvoice_UntilLaunchedOrSkipped()
    {
        // Hoje = 2026-11-21; janela de 4 meses: 2026-11 a 2027-02.
        var owner = Guid.NewGuid();
        var pf = _factory.Cards.AddAccount(owner, "Personal", openingBalance: 1000m, openingBalanceOn: "2026-11-20");
        var card = _factory.Cards.Add(owner, closingDay: 10, dueDay: 17, scope: "Personal", accountId: pf.Id);
        var feed = _factory.EventFeed;

        // Salario no dia 5: a de novembro ja passou (fica no "a lancar"), entra de dezembro em diante.
        var salary = Guid.NewGuid();
        feed.RecurrenceSaved(salary, owner, 5000m, "2026-11-05", transactionType: "Income", paymentMethod: "Pix", accountId: pf.Id,
            description: "Salario");
        // Aluguel PJ no dia 25: dezembro pulado, janeiro lancado (com outro valor), fevereiro pulado e reaberto.
        var rent = Guid.NewGuid();
        feed.RecurrenceSaved(rent, owner, 2000m, "2026-11-25", scope: "Business");
        feed.OccurrenceClosed(rent, owner, "2026-12");
        var januaryRent = Guid.NewGuid();
        feed.Registered(januaryRent, owner, "Business", 2100m, "Moradia", occurredOn: "2027-01-25", paymentMethod: "Boleto");
        feed.OccurrenceClosed(rent, owner, "2027-01", januaryRent);
        feed.OccurrenceClosed(rent, owner, "2027-02");
        feed.OccurrenceReopened(rent, owner, "2027-02");
        // Assinatura no cartao no dia 28: depois do fechamento (10), cai na fatura do mes seguinte.
        feed.RecurrenceSaved(Guid.NewGuid(), owner, 40m, "2026-11-28", cardId: card.Id, accountId: pf.Id, description: "Streaming");
        // Regra excluida nao entra.
        var gone = Guid.NewGuid();
        feed.RecurrenceSaved(gone, owner, 999m, "2026-11-22");
        feed.RecurrenceDeleted(gone, owner);
        // Regra de outro usuario nao entra.
        feed.RecurrenceSaved(Guid.NewGuid(), Guid.NewGuid(), 777m, "2026-11-22");
        await _factory.DrainFeedAsync();

        var body = await ClientFor(owner).GetFromJsonAsync<JsonElement>("/reports/cash-forecast?months=4");

        var m = body.GetProperty("months").EnumerateArray().ToList();
        Assert.Equal((0m, 0m, 0m, 0m, 0m, 0m), Recurring(m[0], "personal"));
        Assert.All(m.Skip(1), month => Assert.Equal((5000m, 0m, 40m, 5000m, 0m, 40m), Recurring(month, "personal")));
        Assert.Equal((0m, 2000m, 0m, 0m, 2000m, 0m), Recurring(m[0], "business"));
        Assert.Equal((0m, 0m, 0m, 0m, 0m, 0m), Recurring(m[1], "business"));
        Assert.Equal((0m, 2100m, 0m, 0m, 0m, 0m), Recurring(m[2], "business"));
        Assert.Equal((0m, 2000m, 0m, 0m, 2000m, 0m), Recurring(m[3], "business"));
        Assert.Equal(5000m - 40m, m[1].GetProperty("personal").GetProperty("result").GetDecimal());
        Assert.Equal((5000m, 0m, 40m, 5000m, 0m, 40m), Recurring(m[1], "total"));
        // Saldo PF: 1000 em 20/11; dezembro +5000 do salario e -40 da fatura da assinatura.
        Assert.Equal([1000m, 5960m, 10920m, 15880m], m.Select(x => Balance(x, "personal")));
    }

    [Fact]
    public async Task Balance_IsNullEverywhere_WhenNoAccountHasAnInformedBalance()
    {
        var owner = Guid.NewGuid();
        _factory.Cards.AddAccount(owner, "Personal");
        _factory.Cards.AddAccount(owner, "Business");

        var body = await ClientFor(owner).GetFromJsonAsync<JsonElement>("/reports/cash-forecast?months=1");

        var month = Assert.Single(body.GetProperty("months").EnumerateArray());
        Assert.Equal(((decimal?)null, (decimal?)null, (decimal?)null),
            (Balance(month, "personal"), Balance(month, "business"), Balance(month, "total")));
        Assert.Equal(2, body.GetProperty("accountsWithoutBalance").GetInt32());
    }
}

/// <summary>INV-8: o client do accounts junta cartoes e contas para dar o escopo de cada cartao.</summary>
public sealed class AccountsCardDirectoryListTests
{
    [Fact]
    public async Task ListWithScopes_JoinsCardsWithTheirAccountScope_ForwardingTheToken()
    {
        var (pf, pj, card1, card2) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var handler = new RoutedAccounts(new Dictionary<string, string>
        {
            ["/credit-cards"] = $$"""
                [{"id":"{{card1}}","accountId":"{{pf}}","label":"PF","lastFourDigits":"1111","creditLimit":1000,"closingDay":10,"dueDay":17,"isActive":true},
                 {"id":"{{card2}}","accountId":"{{pj}}","label":"PJ","lastFourDigits":"2222","creditLimit":9000,"closingDay":5,"dueDay":15,"isActive":false}]
                """,
            ["/accounts"] = $$"""[{"id":"{{pf}}","scope":"Personal"},{"id":"{{pj}}","scope":"Business"}]""",
        });
        var directory = new Expense.Tracking.Reporting.Infrastructure.Clients.AccountsCardDirectory(
            new HttpClient(handler) { BaseAddress = new Uri("http://accounts/") }, new Token("abc"));

        var cards = await directory.ListWithScopesAsync(CancellationToken.None);

        Assert.Equal([(card1, "Personal"), (card2, "Business")], cards.Select(c => (c.Card.Id, c.Scope)));
        Assert.All(handler.Authorizations, a => Assert.Equal("Bearer abc", a));
        Assert.Equal(2, handler.Authorizations.Count);
    }

    [Fact]
    public async Task ListAccounts_ReadsScopeAndOpeningBalance_ForwardingTheToken()
    {
        var (pf, pj) = (Guid.NewGuid(), Guid.NewGuid());
        var handler = new RoutedAccounts(new Dictionary<string, string>
        {
            ["/accounts"] = $$"""
                [{"id":"{{pf}}","scope":"Personal","isActive":true,"openingBalance":-12.5,"openingBalanceOn":"2026-11-10"},
                 {"id":"{{pj}}","scope":"Business","isActive":false,"openingBalance":null,"openingBalanceOn":null}]
                """,
        });
        var directory = new Expense.Tracking.Reporting.Infrastructure.Clients.AccountsCardDirectory(
            new HttpClient(handler) { BaseAddress = new Uri("http://accounts/") }, new Token("abc"));

        var accounts = await directory.ListAccountsAsync(CancellationToken.None);

        Assert.Equal(
            [new Expense.Tracking.Reporting.Application.Forecast.AccountInfo(pf, "Personal", true, -12.5m, new DateOnly(2026, 11, 10)),
             new Expense.Tracking.Reporting.Application.Forecast.AccountInfo(pj, "Business", false, null, null)],
            accounts);
        Assert.Equal("Bearer abc", Assert.Single(handler.Authorizations));
    }

    private sealed class Token(string token) : Expense.Tracking.Reporting.Application.Abstractions.IUserTokenAccessor
    {
        public string? GetAccessToken() => token;
    }

    private sealed class RoutedAccounts(Dictionary<string, string> bodies) : HttpMessageHandler
    {
        public System.Collections.Concurrent.ConcurrentBag<string> Authorizations { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorizations.Add(request.Headers.Authorization!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(bodies[request.RequestUri!.AbsolutePath], System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
