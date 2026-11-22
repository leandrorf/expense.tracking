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
