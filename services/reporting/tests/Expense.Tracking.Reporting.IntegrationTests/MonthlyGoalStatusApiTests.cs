using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Reporting.IntegrationTests;

/// <summary>
/// MET-3 (ADR-0013, regras 2, 4 e 5): situacao da meta mensal e "ainda pode gastar", com os numeros da previsao.
/// Hoje = 2026-11-21 (relogio do <see cref="InvoicesApiFactory"/>); cartao fecha dia 10 e vence dia 17.
/// </summary>
public sealed class MonthlyGoalStatusApiTests(InvoicesApiFactory factory) : IClassFixture<InvoicesApiFactory>
{
    private readonly InvoicesApiFactory _factory = factory;

    private HttpClient ClientFor(Guid owner)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, owner.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, "expenses.read expenses.write");
        return client;
    }

    private static async Task<string> ProblemCode(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()!;

    [Fact]
    public async Task Status_ComparesTheForecastResultWithTheGoal_AndASpendingLowersWhatIsLeft()
    {
        var owner = Guid.NewGuid();
        var client = ClientFor(owner);
        var card = _factory.Cards.Add(owner, closingDay: 10, dueDay: 17, scope: "Personal");
        var feed = _factory.EventFeed;
        // PF em 2026-11: 5.000 de entrada, 1.500 de boleto e a fatura de novembro com 2.000 (compra de 05/11).
        feed.Registered(Guid.NewGuid(), owner, "Personal", 5000m, "Salario", occurredOn: "2026-11-05", transactionType: "Income");
        feed.Registered(Guid.NewGuid(), owner, "Personal", 1500m, "Aluguel", occurredOn: "2026-11-10", paymentMethod: "Boleto");
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 2000m, "2026-11-05");
        // Compra depois do fechamento: entra na fatura de dezembro, nao pesa em novembro.
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 900m, "2026-11-20");
        await _factory.DrainFeedAsync();
        await client.PutAsJsonAsync("/goals/monthly/personal", new { kind = "SaveAmount", amount = 1000m, fromMonth = "2026-11" });

        var before = await client.GetFromJsonAsync<JsonElement>("/goals/monthly/status");

        Assert.Equal("2026-11", before.GetProperty("month").GetString());
        var pf = before.GetProperty("personal");
        Assert.Equal(
            ("SaveAmount", 1000m, 5000m, 1500m, 2000m, 3500m, 1500m, 500m, "Inside"),
            (pf.GetProperty("kind").GetString(), pf.GetProperty("target").GetDecimal(), pf.GetProperty("income").GetDecimal(),
                pf.GetProperty("expense").GetDecimal(), pf.GetProperty("invoices").GetDecimal(), pf.GetProperty("outflows").GetDecimal(),
                pf.GetProperty("result").GetDecimal(), pf.GetProperty("canStillSpend").GetDecimal(), pf.GetProperty("situation").GetString()));
        var forecast = await client.GetFromJsonAsync<JsonElement>("/reports/cash-forecast?months=1");
        Assert.Equal(forecast.GetProperty("months")[0].GetProperty("personal").GetProperty("result").GetDecimal(), pf.GetProperty("result").GetDecimal());

        // Sem meta no PJ: os numeros do mes vem, a situacao nao.
        var pj = before.GetProperty("business");
        Assert.Equal(JsonValueKind.Null, pj.GetProperty("situation").ValueKind);
        Assert.Equal(JsonValueKind.Null, pj.GetProperty("canStillSpend").ValueKind);
        Assert.Equal(0m, pj.GetProperty("result").GetDecimal());

        feed.Registered(Guid.NewGuid(), owner, "Personal", 350m, "Mercado", occurredOn: "2026-11-21");
        await _factory.DrainFeedAsync();
        var after = (await client.GetFromJsonAsync<JsonElement>("/goals/monthly/status")).GetProperty("personal");

        Assert.Equal((150m, "Attention"), (after.GetProperty("canStillSpend").GetDecimal(), after.GetProperty("situation").GetString()));
    }

    [Fact]
    public async Task Reminders_WarnAboutAGoalOutside_AndAnObjectiveBehindNearTheDeadline_UntilTheyRecover()
    {
        // MET-9: hoje = 2026-11-21. PF: entradas 1.000 e saidas 900, meta de guardar 500 -> fora.
        var owner = Guid.NewGuid();
        var client = ClientFor(owner);
        var feed = _factory.EventFeed;
        feed.Registered(Guid.NewGuid(), owner, "Personal", 1000m, "Salario", occurredOn: "2026-11-05", transactionType: "Income");
        feed.Registered(Guid.NewGuid(), owner, "Personal", 900m, "Mercado", occurredOn: "2026-11-06");
        await _factory.DrainFeedAsync();
        await client.PutAsJsonAsync("/goals/monthly/personal", new { kind = "SaveAmount", amount = 500m, fromMonth = "2026-11" });
        // Objetivo de 3.000 ate jan/2027 (3 meses) sem aporte: precisa 1.000 por mes, ritmo 0 -> atrasado.
        var objective = await (await client.PostAsJsonAsync("/goals/objectives", new { scope = "personal", name = "Viagem", target = 3000m, deadline = "2027-01" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var objectiveId = objective.GetProperty("id").GetGuid();

        var items = (await client.GetFromJsonAsync<JsonElement>("/reports/reminders")).GetProperty("items").EnumerateArray().ToList();

        var goal = Assert.Single(items, i => i.GetProperty("kind").GetString() == "MonthlyGoal");
        Assert.Equal(("goal:personal:2026-11:outside", "Overdue", 100m, 500m, "personal"),
            (goal.GetProperty("key").GetString(), goal.GetProperty("urgency").GetString(), goal.GetProperty("amount").GetDecimal(),
                goal.GetProperty("limit").GetDecimal(), goal.GetProperty("scope").GetString()));
        var behind = Assert.Single(items, i => i.GetProperty("kind").GetString() == "SavingsObjective");
        Assert.Equal((objectiveId, 1000m), (behind.GetProperty("objectiveId").GetGuid(), behind.GetProperty("amount").GetDecimal()));

        // Uma receita de 500 deixa resultado 600, sobra 100 (atencao, nao fora), e um aporte de 3.000 atinge o objetivo.
        feed.Registered(Guid.NewGuid(), owner, "Personal", 500m, "Extra", occurredOn: "2026-11-20", transactionType: "Income");
        await _factory.DrainFeedAsync();
        await client.PostAsJsonAsync($"/goals/objectives/{objectiveId}/contributions", new { date = "2026-11-21", amount = 3000m });

        var after = (await client.GetFromJsonAsync<JsonElement>("/reports/reminders")).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("kind").GetString()).ToList();
        Assert.DoesNotContain("MonthlyGoal", after);
        Assert.DoesNotContain("SavingsObjective", after);
    }

    [Fact]
    public async Task Status_OfAFutureMonth_UsesThatMonthTerm_AndItsInvoices()
    {
        var owner = Guid.NewGuid();
        var client = ClientFor(owner);
        var card = _factory.Cards.Add(owner, closingDay: 10, dueDay: 17, scope: "Business");
        _factory.EventFeed.CardPurchase(Guid.NewGuid(), owner, card.Id, 6200m, "2026-11-20");
        await _factory.DrainFeedAsync();
        await client.PutAsJsonAsync("/goals/monthly/business", new { kind = "SaveAmount", amount = 500m, fromMonth = "2026-11" });
        await client.PutAsJsonAsync("/goals/monthly/business", new { kind = "SpendCeiling", amount = 6000m, fromMonth = "2026-12" });

        var december = (await client.GetFromJsonAsync<JsonElement>("/goals/monthly/status?month=2026-12")).GetProperty("business");

        Assert.Equal(
            ("SpendCeiling", 6000m, 6200m, 0m, "Outside"),
            (december.GetProperty("kind").GetString(), december.GetProperty("target").GetDecimal(),
                december.GetProperty("outflows").GetDecimal(), december.GetProperty("canStillSpend").GetDecimal(),
                december.GetProperty("situation").GetString()));
    }

    [Fact]
    public async Task History_ShowsTheClosedMonths_WithTheGoalThatApplied_AndWhetherItWasAchieved()
    {
        // MET-4: hoje = 2026-11-21; os 3 meses fechados sao ago, set e out/2026.
        var owner = Guid.NewGuid();
        var client = ClientFor(owner);
        var card = _factory.Cards.Add(owner, closingDay: 10, dueDay: 17, scope: "Personal");
        var feed = _factory.EventFeed;
        feed.Registered(Guid.NewGuid(), owner, "Personal", 3000m, "Salario", occurredOn: "2026-09-05", transactionType: "Income");
        feed.Registered(Guid.NewGuid(), owner, "Personal", 1500m, "Aluguel", occurredOn: "2026-09-10", paymentMethod: "Boleto");
        feed.Registered(Guid.NewGuid(), owner, "Personal", 3000m, "Salario", occurredOn: "2026-10-05", transactionType: "Income");
        // Compra de 20/09 depois do fechamento: fatura que vence em 17/10.
        feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 2500m, "2026-09-20");
        await _factory.DrainFeedAsync();
        await client.PutAsJsonAsync("/goals/monthly/personal", new { kind = "SaveAmount", amount = 1000m, fromMonth = "2026-09" });

        var history = await client.GetFromJsonAsync<JsonElement[]>("/goals/monthly/history?months=3");

        Assert.Equal(
            [("2026-08", 0m, "null"), ("2026-09", 1500m, "true"), ("2026-10", 500m, "false")],
            history!.Select(m => (m.GetProperty("month").GetString()!, m.GetProperty("personal").GetProperty("result").GetDecimal(),
                m.GetProperty("personal").GetProperty("achieved").GetRawText())).ToArray());
        var october = history![2].GetProperty("personal");
        Assert.Equal((1000m, 2500m, 2500m), (october.GetProperty("target").GetDecimal(), october.GetProperty("invoices").GetDecimal(),
            october.GetProperty("outflows").GetDecimal()));
        Assert.Equal(6, (await client.GetFromJsonAsync<JsonElement[]>("/goals/monthly/history"))!.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public async Task History_OutsideOneTo24Months_IsRejected(int months)
    {
        var response = await ClientFor(Guid.NewGuid()).GetAsync($"/goals/monthly/history?months={months}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("goal.months_invalid", await ProblemCode(response));
    }

    [Theory]
    [InlineData("2026-10", "goal.month_in_past")]
    [InlineData("2027-11", "goal.month_out_of_range")]
    [InlineData("nov", "goal.month_invalid")]
    public async Task Status_OutsideTheForecastWindow_IsRejected(string month, string code)
    {
        var response = await ClientFor(Guid.NewGuid()).GetAsync($"/goals/monthly/status?month={month}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await ProblemCode(response));
    }

    [Fact]
    public async Task Status_OfTheLastForecastMonth_IsAccepted()
    {
        var response = await ClientFor(Guid.NewGuid()).GetAsync("/goals/monthly/status?month=2027-10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
