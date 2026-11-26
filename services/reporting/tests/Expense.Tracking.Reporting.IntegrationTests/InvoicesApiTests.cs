using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Invoices;
using Expense.Tracking.Reporting.Infrastructure.Clients;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Tracking.Reporting.IntegrationTests;

/// <summary>Relogio parado em 2026-11-21 12:00 de Sao Paulo, para as situacoes das faturas.</summary>
public sealed class InvoicesApiFactory : ReportingApiFactory
{
    public static readonly DateTimeOffset Now = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now)));
    }
}

/// <summary>
/// INV-4 (ADR-0005): faturas de um cartao pela API, com a projecao alimentada pelo feed falso e o
/// cartao vindo do "accounts" falso (so para o dono). Hoje = 2026-11-21; cartao fecha dia 10 e vence
/// dia 17, entao a fatura atual e a de 2026-12.
/// </summary>
public sealed class InvoicesApiTests(InvoicesApiFactory factory) : IClassFixture<InvoicesApiFactory>
{
    private readonly InvoicesApiFactory _factory = factory;

    private HttpClient ClientFor(Guid owner)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, owner.ToString());
        return client;
    }

    private static List<JsonElement> InvoicesOf(JsonElement body) => body.GetProperty("invoices").EnumerateArray().ToList();

    [Fact]
    public async Task Invoices_FollowTheCycleOfEachPeriod_AfterACycleChange()
    {
        // CYC-4: fecha 10/vence 17 ate a fatura de 2027-01; a partir de 2027-02, fecha 25 e vence 5 do mes seguinte.
        // A fatura da troca (2027-02) vai de 11/01 (dia seguinte ao fechamento antigo de janeiro) a 25/01, e vence 05/02.
        var owner = Guid.NewGuid();
        var card = _factory.Cards.Add(owner, closingDay: 10, dueDay: 17,
            cycles: [new CardCycleInfo(null, 10, 17), new CardCycleInfo("2027-02", 25, 5)]);
        var feed = _factory.EventFeed;
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 100m, "2026-12-05", description: "Antes do fechamento");
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 40m, "2026-12-20", description: "Ultima fatura do ciclo antigo");
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 60m, "2027-01-20", description: "Na fatura da troca");
        feed.InvoicePaid(Guid.NewGuid(), owner, card.Id, "2026-12", 100m);
        await _factory.DrainFeedAsync();

        var invoices = InvoicesOf(await ClientFor(owner).GetFromJsonAsync<JsonElement>($"/reports/cards/{card.Id}/invoices?from=2026-12"));

        Assert.Equal(
            [("2026-12", "2026-11-11", "2026-12-10", "2026-12-17", 100m, 100m),
             ("2027-01", "2026-12-11", "2027-01-10", "2027-01-17", 40m, 0m),
             ("2027-02", "2027-01-11", "2027-01-25", "2027-02-05", 60m, 0m)],
            invoices.Take(3).Select(i => (i.GetProperty("dueMonth").GetString(), i.GetProperty("startDate").GetString(),
                i.GetProperty("closingDate").GetString(), i.GetProperty("dueDate").GetString(), i.GetProperty("total").GetDecimal(),
                i.GetProperty("paid").GetDecimal())));
    }

    [Fact]
    public async Task Invoices_GroupPurchasesByCycle_WithInstallmentsRefundsAndStatus()
    {
        var owner = Guid.NewGuid();
        var card = _factory.Cards.Add(owner, closingDay: 10, dueDay: 17);
        var feed = _factory.EventFeed;
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 100m, "2026-10-05", description: "Mercado");
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 200m, "2026-11-10", description: "No dia do fechamento");
        var notebook = Guid.NewGuid();
        feed.CardPurchase(notebook, owner, card.Id, 300m, "2026-11-15", installments: 3, description: "Notebook", category: "Eletronicos");
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 50m, "2026-11-20", description: "Estorno loja", transactionType: "Income");
        // Compra de outro cartao do mesmo dono nao entra.
        feed.CardPurchase(Guid.NewGuid(), owner, Guid.NewGuid(), 999m, "2026-11-15");
        await _factory.DrainFeedAsync();

        var body = await ClientFor(owner).GetFromJsonAsync<JsonElement>($"/reports/cards/{card.Id}/invoices");

        Assert.Equal("2026-12", body.GetProperty("currentDueMonth").GetString());
        Assert.Equal((10, 17, "4242"), (body.GetProperty("closingDay").GetInt32(), body.GetProperty("dueDay").GetInt32(),
            body.GetProperty("lastFourDigits").GetString()));
        var invoices = InvoicesOf(body);
        Assert.Equal(["2026-10", "2026-11", "2026-12", "2027-01", "2027-02"], invoices.Select(i => i.GetProperty("dueMonth").GetString()));
        Assert.Equal(["Overdue", "Overdue", "Open", "Open", "Open"], invoices.Select(i => i.GetProperty("status").GetString()));
        Assert.Equal([100m, 200m, 50m, 100m, 100m], invoices.Select(i => i.GetProperty("total").GetDecimal()));

        var december = invoices[2];
        Assert.Equal(("2026-11-11", "2026-12-10", "2026-12-17"), (december.GetProperty("startDate").GetString(),
            december.GetProperty("closingDate").GetString(), december.GetProperty("dueDate").GetString()));
        var items = december.GetProperty("items").EnumerateArray().ToList();
        var parcel = Assert.Single(items, i => i.GetProperty("transactionId").GetGuid() == notebook);
        Assert.Equal(("Notebook", "Eletronicos", 100m, 1, 3), (parcel.GetProperty("description").GetString(),
            parcel.GetProperty("category").GetString(), parcel.GetProperty("amount").GetDecimal(),
            parcel.GetProperty("installment").GetInt32(), parcel.GetProperty("installments").GetInt32()));
        Assert.Contains(items, i => i.GetProperty("amount").GetDecimal() == -50m && i.GetProperty("description").GetString() == "Estorno loja");
        Assert.All(invoices, i => Assert.Equal(0m, i.GetProperty("paid").GetDecimal()));
    }

    [Fact]
    public async Task EditingAPurchaseFrom3To6Installments_RedistributesTheParcels()
    {
        // LNC-4 (ADR-0007): a fatura le a projecao, que aplica o TransactionUpdated.
        var owner = Guid.NewGuid();
        var card = _factory.Cards.Add(owner, closingDay: 10, dueDay: 17);
        var notebook = Guid.NewGuid();
        _factory.EventFeed.CardPurchase(notebook, owner, card.Id, 300m, "2026-11-15", installments: 3, description: "Notebook");
        _factory.EventFeed.Updated(notebook, owner, 600m, "2026-11-15", cardId: card.Id, installments: 6, description: "Notebook 16GB");
        await _factory.DrainFeedAsync();

        var invoices = InvoicesOf(await ClientFor(owner).GetFromJsonAsync<JsonElement>($"/reports/cards/{card.Id}/invoices"));

        Assert.Equal(["2026-12", "2027-01", "2027-02", "2027-03", "2027-04", "2027-05"],
            invoices.Select(i => i.GetProperty("dueMonth").GetString()));
        Assert.All(invoices, i => Assert.Equal(100m, i.GetProperty("total").GetDecimal()));
        var last = Assert.Single(invoices[^1].GetProperty("items").EnumerateArray());
        Assert.Equal(("Notebook 16GB", 6, 6), (last.GetProperty("description").GetString(),
            last.GetProperty("installment").GetInt32(), last.GetProperty("installments").GetInt32()));
    }

    [Fact]
    public async Task Payments_SettleTheInvoiceTheyName_AndDeletingOneRestoresTheStatus()
    {
        var owner = Guid.NewGuid();
        var card = _factory.Cards.Add(owner, closingDay: 10, dueDay: 17);
        var feed = _factory.EventFeed;
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 100m, "2026-09-05"); // vence 2026-09
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 200m, "2026-10-05"); // vence 2026-10
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 300m, "2026-11-05"); // vence 2026-11 (17/11, ja passou)
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 400m, "2026-11-15"); // vence 2026-12 (aberta)
        var full = Guid.NewGuid();
        feed.InvoicePaid(full, owner, card.Id, "2026-09", 100m, "2026-09-17");
        feed.InvoicePaid(Guid.NewGuid(), owner, card.Id, "2026-10", 50m, "2026-10-17");
        var partial = Guid.NewGuid();
        feed.InvoicePaid(partial, owner, card.Id, "2026-11", 120m, "2026-11-10");
        feed.InvoicePaid(Guid.NewGuid(), owner, card.Id, "2026-11", 180m, "2026-11-17");
        feed.InvoicePaid(Guid.NewGuid(), owner, card.Id, "2026-12", 400m, "2026-11-20"); // antecipado: segue aberta
        // Pagamento de outro cartao do mesmo dono nao entra.
        feed.InvoicePaid(Guid.NewGuid(), owner, Guid.NewGuid(), "2026-10", 999m);
        await _factory.DrainFeedAsync();

        var invoices = InvoicesOf(await ClientFor(owner).GetFromJsonAsync<JsonElement>($"/reports/cards/{card.Id}/invoices"));

        Assert.Equal(["2026-09", "2026-10", "2026-11", "2026-12"], invoices.Select(i => i.GetProperty("dueMonth").GetString()));
        Assert.Equal(["Paid", "Overdue", "Paid", "Open"], invoices.Select(i => i.GetProperty("status").GetString()));
        Assert.Equal([100m, 50m, 300m, 400m], invoices.Select(i => i.GetProperty("paid").GetDecimal()));
        Assert.Equal([0m, 150m, 0m, 0m], invoices.Select(i => i.GetProperty("remaining").GetDecimal()));
        var november = invoices[2].GetProperty("payments").EnumerateArray().ToList();
        Assert.Equal([(partial, 120m, "2026-11-10"), (november[1].GetProperty("paymentId").GetGuid(), 180m, "2026-11-17")],
            november.Select(p => (p.GetProperty("paymentId").GetGuid(), p.GetProperty("amount").GetDecimal(), p.GetProperty("paidOn").GetString())));

        // Excluido o segundo pagamento de novembro, a fatura vencida fica com saldo de novo.
        feed.InvoicePaymentDeleted(november[1].GetProperty("paymentId").GetGuid(), owner, card.Id);
        await _factory.DrainFeedAsync();
        var after = InvoicesOf(await ClientFor(owner).GetFromJsonAsync<JsonElement>($"/reports/cards/{card.Id}/invoices"));
        Assert.Equal(("Overdue", 120m, 180m), (after[2].GetProperty("status").GetString(),
            after[2].GetProperty("paid").GetDecimal(), after[2].GetProperty("remaining").GetDecimal()));
    }

    [Fact]
    public async Task PaymentOfAClosedInvoice_MakesItPartiallyPaid()
    {
        // Hoje 21/11; cartao fecha dia 20 e vence dia 28: a fatura de 2026-11 fechou ontem e vence dia 28.
        var owner = Guid.NewGuid();
        var card = _factory.Cards.Add(owner, closingDay: 20, dueDay: 28);
        _factory.EventFeed.CardPurchase(Guid.NewGuid(), owner, card.Id, 500m, "2026-11-01");
        _factory.EventFeed.InvoicePaid(Guid.NewGuid(), owner, card.Id, "2026-11", 200m);
        await _factory.DrainFeedAsync();

        var invoices = InvoicesOf(await ClientFor(owner).GetFromJsonAsync<JsonElement>($"/reports/cards/{card.Id}/invoices"));

        var november = Assert.Single(invoices, i => i.GetProperty("dueMonth").GetString() == "2026-11");
        Assert.Equal(("PartiallyPaid", 200m, 300m), (november.GetProperty("status").GetString(),
            november.GetProperty("paid").GetDecimal(), november.GetProperty("remaining").GetDecimal()));
    }

    [Fact]
    public async Task Payment_NeverCountsAsSpending_InTheSummaryOrBudgets()
    {
        var owner = Guid.NewGuid();
        var card = _factory.Cards.Add(owner, closingDay: 10, dueDay: 17);
        _factory.EventFeed.InvoicePaid(Guid.NewGuid(), owner, card.Id, "2026-11", 1500m, "2026-11-17");
        await _factory.DrainFeedAsync();
        var client = ClientFor(owner);

        var summary = await client.GetFromJsonAsync<JsonElement>("/reports/summary");
        var budgets = await client.GetFromJsonAsync<JsonElement>("/budgets/status?month=2026-11");

        Assert.Equal((0m, 0), (summary.GetProperty("totalExpense").GetDecimal(), summary.GetProperty("count").GetInt32()));
        Assert.Empty(budgets.GetProperty("unbudgeted").EnumerateArray());
    }

    [Fact]
    public async Task CurrentInvoice_IsPresentEvenWithoutPurchases()
    {
        var owner = Guid.NewGuid();
        var card = _factory.Cards.Add(owner, closingDay: 25, dueDay: 5);

        var body = await ClientFor(owner).GetFromJsonAsync<JsonElement>($"/reports/cards/{card.Id}/invoices");

        var invoice = Assert.Single(InvoicesOf(body));
        // Fecha dia 25 e vence dia 5: em 21/11, a fatura atual fecha em 25/11 e vence em 05/12.
        Assert.Equal(("2026-12", "Open", 0m, "2026-11-25", "2026-12-05"), (invoice.GetProperty("dueMonth").GetString(),
            invoice.GetProperty("status").GetString(), invoice.GetProperty("total").GetDecimal(),
            invoice.GetProperty("closingDate").GetString(), invoice.GetProperty("dueDate").GetString()));
    }

    [Fact]
    public async Task FromAndTo_CutByDueMonth()
    {
        var owner = Guid.NewGuid();
        var card = _factory.Cards.Add(owner, closingDay: 10, dueDay: 17);
        _factory.EventFeed.CardPurchase(Guid.NewGuid(), owner, card.Id, 1200m, "2026-11-15", installments: 12);
        await _factory.DrainFeedAsync();

        var body = await ClientFor(owner).GetFromJsonAsync<JsonElement>($"/reports/cards/{card.Id}/invoices?from=2027-02&to=2027-04");

        Assert.Equal(["2027-02", "2027-03", "2027-04"], InvoicesOf(body).Select(i => i.GetProperty("dueMonth").GetString()));
    }

    [Fact]
    public async Task DeletedPurchase_LeavesEveryInvoice()
    {
        var owner = Guid.NewGuid();
        var card = _factory.Cards.Add(owner, closingDay: 10, dueDay: 17);
        var purchase = Guid.NewGuid();
        _factory.EventFeed.CardPurchase(purchase, owner, card.Id, 300m, "2026-11-15", installments: 3);
        _factory.EventFeed.Deleted(purchase, owner);
        await _factory.DrainFeedAsync();

        var body = await ClientFor(owner).GetFromJsonAsync<JsonElement>($"/reports/cards/{card.Id}/invoices");

        var invoice = Assert.Single(InvoicesOf(body)); // so a atual, vazia
        Assert.Equal(0m, invoice.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task AnotherUsersOrUnknownCard_IsNotFound_WithoutTokenUnauthorized_AndBadMonthRejected()
    {
        var card = _factory.Cards.Add(Guid.NewGuid(), closingDay: 10, dueDay: 17);
        var stranger = ClientFor(Guid.NewGuid());

        var other = await stranger.GetAsync($"/reports/cards/{card.Id}/invoices");
        var unknown = await stranger.GetAsync($"/reports/cards/{Guid.NewGuid()}/invoices");
        var anonymous = await _factory.CreateClient().GetAsync($"/reports/cards/{card.Id}/invoices");
        var badMonth = await stranger.GetAsync($"/reports/cards/{card.Id}/invoices?from=11-2026");

        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal("card.not_found", (await other.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badMonth.StatusCode);
    }
}

/// <summary>INV-4: o client do accounts encaminha o token do usuario e traduz o 404.</summary>
public sealed class AccountsCardDirectoryTests
{
    [Fact]
    public async Task ForwardsTheUserToken_AndReadsTheCard()
    {
        var cardId = Guid.NewGuid();
        var handler = new StubAccounts(HttpStatusCode.OK,
            $$"""{"id":"{{cardId}}","accountId":"{{Guid.NewGuid()}}","label":"Nubank","brand":"Visa","lastFourDigits":"1234","creditLimit":5000,"closingDay":10,"dueDay":17,"isActive":true}""");
        var directory = new AccountsCardDirectory(new HttpClient(handler) { BaseAddress = new Uri("http://accounts/") }, new Token("abc"));

        var card = await directory.FindAsync(cardId, CancellationToken.None);

        Assert.Equal(("Nubank", 10, 17, 5000m), (card!.Label, card.ClosingDay, card.DueDay, card.CreditLimit));
        Assert.Equal($"http://accounts/credit-cards/{cardId}", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer abc", handler.Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task NotFound_IsNull_AndOtherFailuresThrow()
    {
        var notFound = new AccountsCardDirectory(
            new HttpClient(new StubAccounts(HttpStatusCode.NotFound, "{}")) { BaseAddress = new Uri("http://accounts/") }, new Token("abc"));
        var failing = new AccountsCardDirectory(
            new HttpClient(new StubAccounts(HttpStatusCode.ServiceUnavailable, "{}")) { BaseAddress = new Uri("http://accounts/") }, new Token("abc"));

        Assert.Null(await notFound.FindAsync(Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => failing.FindAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private sealed class Token(string token) : IUserTokenAccessor
    {
        public string? GetAccessToken() => token;
    }

    private sealed class StubAccounts(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}

/// <summary>
/// INV-10 (ADR-0005, decisao 9): limite disponivel = limite − o que falta pagar de todas as faturas,
/// incluindo as parcelas futuras. Hoje = 2026-11-21; cartao fecha dia 10 e vence dia 17.
/// </summary>
public sealed class CardLimitsApiTests(InvoicesApiFactory factory) : IClassFixture<InvoicesApiFactory>
{
    private readonly InvoicesApiFactory _factory = factory;

    private HttpClient ClientFor(Guid owner)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, owner.ToString());
        return client;
    }

    [Fact]
    public async Task AvailableLimit_SubtractsWhatIsStillUnpaid_IncludingFutureInstallments()
    {
        var owner = Guid.NewGuid();
        var card = _factory.Cards.Add(owner, closingDay: 10, dueDay: 17, limit: 5000m);
        var other = _factory.Cards.Add(owner, closingDay: 10, dueDay: 17, limit: 300m);
        var feed = _factory.EventFeed;
        // Fatura de 2026-10 (vencida): 1000, com 400 pagos → faltam 600.
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 1000m, "2026-10-05");
        feed.InvoicePaid(Guid.NewGuid(), owner, card.Id, "2026-10", 400m);
        // Fatura de 2026-09: 100, com 300 pagos → falta 0 (o que passou nao abate as outras).
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 100m, "2026-09-05");
        feed.InvoicePaid(Guid.NewGuid(), owner, card.Id, "2026-09", 300m);
        // 12x de 100 a partir de 2026-12, e um estorno de 50 na fatura de 2026-12 → 1150.
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 1200m, "2026-11-15", installments: 12);
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 50m, "2026-11-20", transactionType: "Income");
        // Outro cartao, acima do limite: disponivel negativo, nada e bloqueado.
        feed.CardPurchase(Guid.NewGuid(), owner, other.Id, 450m, "2026-11-15");
        await _factory.DrainFeedAsync();
        var client = ClientFor(owner);

        var limits = await client.GetFromJsonAsync<JsonElement[]>("/reports/cards/limits");
        var invoices = await client.GetFromJsonAsync<JsonElement>($"/reports/cards/{card.Id}/invoices?from=2026-12&to=2026-12");

        Assert.Equal(2, limits!.Length);
        var main = Assert.Single(limits, l => l.GetProperty("cardId").GetGuid() == card.Id);
        Assert.Equal((5000m, 1750m, 3250m), (main.GetProperty("creditLimit").GetDecimal(),
            main.GetProperty("outstanding").GetDecimal(), main.GetProperty("availableLimit").GetDecimal()));
        var over = Assert.Single(limits, l => l.GetProperty("cardId").GetGuid() == other.Id);
        Assert.Equal(-150m, over.GetProperty("availableLimit").GetDecimal());
        // A fatura traz o mesmo limite, sem depender do recorte from/to.
        Assert.Equal((5000m, 1750m, 3250m), (invoices.GetProperty("creditLimit").GetDecimal(),
            invoices.GetProperty("outstanding").GetDecimal(), invoices.GetProperty("availableLimit").GetDecimal()));
    }

    [Fact]
    public async Task Limits_AreOnlyTheUsersCards_AndNeedAToken()
    {
        _factory.Cards.Add(Guid.NewGuid(), closingDay: 10, dueDay: 17);

        var mine = await ClientFor(Guid.NewGuid()).GetFromJsonAsync<JsonElement[]>("/reports/cards/limits");
        var anonymous = await _factory.CreateClient().GetAsync("/reports/cards/limits");

        Assert.Empty(mine!);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}
