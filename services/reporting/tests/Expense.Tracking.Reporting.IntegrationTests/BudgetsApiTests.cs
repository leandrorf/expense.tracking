using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Reporting.IntegrationTests;

/// <summary>
/// BDG-2/BDG-3/BDG-4: definir, listar, alterar o valor, encerrar e acompanhar orcamentos. Os testes dividem o
/// banco; cada um usa um usuario proprio.
/// </summary>
public sealed class BudgetsApiTests(ReportingApiFactory factory) : IClassFixture<ReportingApiFactory>
{
    private readonly ReportingApiFactory _factory = factory;

    private HttpClient ClientFor(Guid owner, string scope = "expenses.read expenses.write")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, owner.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, scope);
        return client;
    }

    private static object Budget(
        string category = "Alimentação", string scope = "Personal", decimal amount = 1500m,
        string startMonth = "2026-12", string? currency = null) =>
        new { category, scope, amount, startMonth, currency };

    [Fact]
    public async Task Create_ThenList_ShowsTheBudgetOnlyToItsOwner()
    {
        var owner = Guid.NewGuid();
        var client = ClientFor(owner);

        var response = await client.PostAsJsonAsync("/budgets", Budget());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Alimentação", created.GetProperty("category").GetString());
        Assert.Equal("Personal", created.GetProperty("scope").GetString());
        Assert.Equal(1500m, created.GetProperty("amount").GetDecimal());
        Assert.Equal("BRL", created.GetProperty("currency").GetString());
        Assert.Equal("2026-12", created.GetProperty("startMonth").GetString());
        Assert.Equal($"/budgets/{created.GetProperty("id").GetGuid()}", response.Headers.Location?.ToString());

        var mine = await client.GetFromJsonAsync<JsonElement[]>("/budgets");
        Assert.Single(mine!);

        var others = await ClientFor(Guid.NewGuid()).GetFromJsonAsync<JsonElement[]>("/budgets");
        Assert.Empty(others!);
    }

    [Fact]
    public async Task SameCategory_InTheOtherScope_CoexistsAsAnIndependentBudget()
    {
        var client = ClientFor(Guid.NewGuid());

        var pf = await client.PostAsJsonAsync("/budgets", Budget(scope: "Personal"));
        var pj = await client.PostAsJsonAsync("/budgets", Budget(scope: "Business", amount: 3000m));

        Assert.Equal(HttpStatusCode.Created, pf.StatusCode);
        Assert.Equal(HttpStatusCode.Created, pj.StatusCode);
        var list = await client.GetFromJsonAsync<JsonElement[]>("/budgets");
        Assert.Equal(
            [("Personal", 1500m), ("Business", 3000m)],
            list!.Select(b => (b.GetProperty("scope").GetString(), b.GetProperty("amount").GetDecimal())).ToArray());
    }

    [Theory]
    [InlineData("alimentacao")]
    [InlineData("  ALIMENTAÇÃO  ")]
    [InlineData("Alimentação")]
    public async Task SameNormalizedCategory_InTheSameScope_IsAConflict(string variant)
    {
        var client = ClientFor(Guid.NewGuid());
        await client.PostAsJsonAsync("/budgets", Budget(category: "Alimentação"));

        var response = await client.PostAsJsonAsync("/budgets", Budget(category: variant));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("budget.duplicate", problem.GetProperty("title").GetString());
    }

    public static TheoryData<object, string> InvalidRequests() => new()
    {
        { Budget(amount: 0m), "budget.amount_invalid" },
        { Budget(amount: -10m), "budget.amount_invalid" },
        { Budget(amount: 10.555m), "budget.amount_invalid" },
        { Budget(category: ""), "budget.category_invalid" },
        { Budget(category: "   "), "budget.category_invalid" },
        { Budget(category: new string('x', 61)), "budget.category_invalid" },
        { Budget(scope: "PF"), "budget.scope_invalid" },
        { Budget(scope: "1"), "budget.scope_invalid" },
        { Budget(startMonth: "2026-13"), "budget.month_invalid" },
        { Budget(startMonth: "12/2026"), "budget.month_invalid" },
        { Budget(currency: "REAL"), "budget.currency_invalid" },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidRequest_ReturnsValidationProblem(object request, string expectedCode)
    {
        var response = await ClientFor(Guid.NewGuid()).PostAsJsonAsync("/budgets", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedCode, problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Create_WithoutWriteScope_ReturnsForbidden()
    {
        var response = await ClientFor(Guid.NewGuid(), scope: "expenses.read").PostAsJsonAsync("/budgets", Budget());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_WithReadScopeOnly_IsAllowed()
    {
        var response = await ClientFor(Guid.NewGuid(), scope: "expenses.read").GetAsync("/budgets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task WithoutToken_ReturnsUnauthorized(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "/budgets")
        {
            Content = method == "POST" ? JsonContent.Create(Budget()) : null
        };

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProcessingTheFeed_KeepsTheBudgets()
    {
        // ADR-0002: o orcamento e dado do usuario; aplicar eventos na projecao nao mexe nele.
        var client = ClientFor(Guid.NewGuid());
        await client.PostAsJsonAsync("/budgets", Budget());

        _factory.EventFeed.Registered(Guid.NewGuid(), Guid.NewGuid(), amount: 10m);
        await _factory.DrainFeedAsync();

        Assert.Single((await client.GetFromJsonAsync<JsonElement[]>("/budgets"))!);
    }
    private static async Task<Guid> CreateAsync(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/budgets", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<decimal?> AmountInAsync(HttpClient client, Guid id, string month)
    {
        var list = await client.GetFromJsonAsync<JsonElement[]>($"/budgets?month={month}");
        var budget = list!.SingleOrDefault(b => b.GetProperty("id").GetGuid() == id);
        return budget.ValueKind == JsonValueKind.Undefined ? null : budget.GetProperty("amount").GetDecimal();
    }

    [Fact]
    public async Task ChangeAmount_AppliesFromTheMonth_AndKeepsThePreviousMonths()
    {
        var client = ClientFor(Guid.NewGuid());
        var id = await CreateAsync(client, Budget(amount: 1500m, startMonth: "2026-12"));

        var response = await client.PutAsJsonAsync($"/budgets/{id}/amount", new { amount = 1800m, fromMonth = "2027-02" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1800m, body.GetProperty("amount").GetDecimal());
        Assert.Equal(
            [("2026-12", 1500m), ("2027-02", 1800m)],
            body.GetProperty("limits").EnumerateArray()
                .Select(l => (l.GetProperty("fromMonth").GetString(), l.GetProperty("amount").GetDecimal())).ToArray());
        Assert.Null(await AmountInAsync(client, id, "2026-11"));
        Assert.Equal(1500m, await AmountInAsync(client, id, "2026-12"));
        Assert.Equal(1500m, await AmountInAsync(client, id, "2027-01"));
        Assert.Equal(1800m, await AmountInAsync(client, id, "2027-02"));
        Assert.Equal(1800m, await AmountInAsync(client, id, "2027-09"));
    }

    [Fact]
    public async Task Close_HidesTheBudgetFromThatMonth_AndKeepsItInThePreviousMonths()
    {
        var client = ClientFor(Guid.NewGuid());
        var id = await CreateAsync(client, Budget(startMonth: "2026-12"));

        var response = await client.PostAsJsonAsync($"/budgets/{id}/close", new { fromMonth = "2027-03" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2027-03", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("closedFromMonth").GetString());
        Assert.Equal(1500m, await AmountInAsync(client, id, "2027-02"));
        Assert.Null(await AmountInAsync(client, id, "2027-03"));
        Assert.Null(await AmountInAsync(client, id, "2027-08"));
        // Sem mes, a lista continua mostrando o orcamento encerrado (historico).
        Assert.Single((await client.GetFromJsonAsync<JsonElement[]>("/budgets"))!);
    }

    [Fact]
    public async Task AfterClosing_TheCategoryCanGetANewBudget_OnlyFromTheClosingMonth()
    {
        var client = ClientFor(Guid.NewGuid());
        var id = await CreateAsync(client, Budget(startMonth: "2026-12"));
        await client.PostAsJsonAsync($"/budgets/{id}/close", new { fromMonth = "2027-03" });

        var overlapping = await client.PostAsJsonAsync("/budgets", Budget(startMonth: "2027-02"));
        var next = await client.PostAsJsonAsync("/budgets", Budget(amount: 900m, startMonth: "2027-03"));

        Assert.Equal(HttpStatusCode.Conflict, overlapping.StatusCode);
        Assert.Equal(HttpStatusCode.Created, next.StatusCode);
        var march = await client.GetFromJsonAsync<JsonElement[]>("/budgets?month=2027-03");
        Assert.Equal(900m, Assert.Single(march!).GetProperty("amount").GetDecimal());
    }

    [Theory]
    [InlineData("PUT", "amount")]
    [InlineData("POST", "close")]
    public async Task ChangingSomeoneElsesBudget_ReturnsNotFound(string method, string action)
    {
        var id = await CreateAsync(ClientFor(Guid.NewGuid()), Budget());
        var request = new HttpRequestMessage(new HttpMethod(method), $"/budgets/{id}/{action}")
        {
            Content = JsonContent.Create(new { amount = 10m, fromMonth = "2027-03" })
        };

        var response = await ClientFor(Guid.NewGuid()).SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChangeAmount_AfterClosing_IsAConflict()
    {
        var client = ClientFor(Guid.NewGuid());
        var id = await CreateAsync(client, Budget(startMonth: "2026-12"));
        await client.PostAsJsonAsync($"/budgets/{id}/close", new { fromMonth = "2027-03" });

        var response = await client.PutAsJsonAsync($"/budgets/{id}/amount", new { amount = 10m, fromMonth = "2027-04" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("budget.closed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("/amount", "2026-11", "budget.month_before_start")]
    [InlineData("/amount", "2027", "budget.month_invalid")]
    [InlineData("/close", "2026-12", "budget.month_before_start")]
    [InlineData("/close", "", "budget.month_invalid")]
    public async Task InvalidChange_ReturnsValidationProblem(string action, string fromMonth, string expectedCode)
    {
        var client = ClientFor(Guid.NewGuid());
        var id = await CreateAsync(client, Budget(startMonth: "2026-12"));
        var method = action == "/amount" ? HttpMethod.Put : HttpMethod.Post;

        var response = await client.SendAsync(new HttpRequestMessage(method, $"/budgets/{id}{action}")
        {
            Content = JsonContent.Create(new { amount = 10m, fromMonth })
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedCode, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Changes_WithoutWriteScope_ReturnForbidden()
    {
        var id = await CreateAsync(ClientFor(Guid.NewGuid()), Budget());
        var reader = ClientFor(Guid.NewGuid(), scope: "expenses.read");

        var change = await reader.PutAsJsonAsync($"/budgets/{id}/amount", new { amount = 10m, fromMonth = "2027-03" });
        var close = await reader.PostAsJsonAsync($"/budgets/{id}/close", new { fromMonth = "2027-03" });

        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, close.StatusCode);
    }

    [Fact]
    public async Task List_WithInvalidMonth_ReturnsValidationProblem()
    {
        var response = await ClientFor(Guid.NewGuid()).GetAsync("/budgets?month=dezembro");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    private async Task<JsonElement[]> StatusAsync(HttpClient client, string month)
    {
        var body = await client.GetFromJsonAsync<JsonElement>($"/budgets/status?month={month}");
        Assert.Equal(month, body.GetProperty("month").GetString());
        return body.GetProperty("budgets").EnumerateArray().ToArray();
    }

    [Fact]
    public async Task Status_ComputesTheMonthFromTheProjection()
    {
        var owner = Guid.NewGuid();
        var client = ClientFor(owner);
        await CreateAsync(client, Budget(category: "Alimentação", scope: "Personal", amount: 1000m, startMonth: "2026-12"));
        await CreateAsync(client, Budget(category: "Fornecedores", scope: "Business", amount: 5000m, startMonth: "2026-12"));
        var feed = _factory.EventFeed;
        // PF: Pix + dinheiro + cartao em contas diferentes, uma variacao de digitacao da categoria.
        feed.Registered(Guid.NewGuid(), owner, "Personal", 300m, "Alimentação", occurredOn: "2026-12-02", paymentMethod: "Pix");
        feed.Registered(Guid.NewGuid(), owner, "Personal", 200m, "alimentacao ", occurredOn: "2026-12-10", paymentMethod: "Cash");
        feed.Registered(Guid.NewGuid(), owner, "Personal", 250m, "Alimentação", occurredOn: "2026-12-28", paymentMethod: "CreditCard");
        // PJ: boleto + transferencia.
        feed.Registered(Guid.NewGuid(), owner, "Business", 3000m, "Fornecedores", occurredOn: "2026-12-05", paymentMethod: "Boleto");
        feed.Registered(Guid.NewGuid(), owner, "Business", 1200m, "Fornecedores", occurredOn: "2026-12-20", paymentMethod: "BankTransfer");
        // Nao entram: gasto PJ na categoria do PF, receita, outro mes, outra moeda, escopo desconhecido, outro dono.
        feed.Registered(Guid.NewGuid(), owner, "Business", 999m, "Alimentação", occurredOn: "2026-12-03");
        feed.Registered(Guid.NewGuid(), owner, "Personal", 999m, "Alimentação", occurredOn: "2026-12-03", transactionType: "Income");
        feed.Registered(Guid.NewGuid(), owner, "Personal", 999m, "Alimentação", occurredOn: "2027-01-01");
        feed.Registered(Guid.NewGuid(), owner, "Personal", 999m, "Alimentação", occurredOn: "2026-12-03", currency: "USD");
        feed.Registered(Guid.NewGuid(), owner, null, 999m, "Alimentação", occurredOn: "2026-12-03");
        feed.Registered(Guid.NewGuid(), Guid.NewGuid(), "Personal", 999m, "Alimentação", occurredOn: "2026-12-03");
        await _factory.DrainFeedAsync();

        var statuses = await StatusAsync(client, "2026-12");

        Assert.Equal(
            [
                ("Alimentação", "Personal", 1000m, 750m, 250m, 75m, "OnTrack"),
                ("Fornecedores", "Business", 5000m, 4200m, 800m, 84m, "Warning"),
            ],
            statuses.Select(s => (
                s.GetProperty("category").GetString(),
                s.GetProperty("scope").GetString(),
                s.GetProperty("limit").GetDecimal(),
                s.GetProperty("spent").GetDecimal(),
                s.GetProperty("remaining").GetDecimal(),
                s.GetProperty("percent").GetDecimal(),
                s.GetProperty("status").GetString())).ToArray());
    }

    [Fact]
    public async Task Status_ListsUnbudgetedSpendingByScope()
    {
        var owner = Guid.NewGuid();
        var client = ClientFor(owner);
        await CreateAsync(client, Budget(category: "Fornecedores", scope: "Business", amount: 5000m, startMonth: "2026-12"));
        await CreateAsync(client, Budget(category: "Viagens", scope: "Personal", amount: 1000m, startMonth: "2026-12"));
        var feed = _factory.EventFeed;
        feed.Registered(Guid.NewGuid(), owner, "Business", 3000m, "Fornecedores", occurredOn: "2026-12-05", paymentMethod: "Boleto");
        // PJ em "Viagens": o orcamento de Viagens e PF, entao fica sem orcamento.
        feed.Registered(Guid.NewGuid(), owner, "Business", 800m, "Viagens", occurredOn: "2026-12-08", paymentMethod: "CreditCard");
        feed.Registered(Guid.NewGuid(), owner, "Business", 200m, "Hospedagem", occurredOn: "2026-12-09");
        feed.Registered(Guid.NewGuid(), owner, "Personal", 400m, "Viagens", occurredOn: "2026-12-10");
        await _factory.DrainFeedAsync();

        var body = await client.GetFromJsonAsync<JsonElement>("/budgets/status?month=2026-12");

        var unbudgeted = Assert.Single(body.GetProperty("unbudgeted").EnumerateArray());
        Assert.Equal("Business", unbudgeted.GetProperty("scope").GetString());
        Assert.Equal("BRL", unbudgeted.GetProperty("currency").GetString());
        Assert.Equal(1000m, unbudgeted.GetProperty("total").GetDecimal());
        Assert.Equal(
            [("Viagens", 800m), ("Hospedagem", 200m)],
            unbudgeted.GetProperty("categories").EnumerateArray()
                .Select(c => (c.GetProperty("category").GetString(), c.GetProperty("total").GetDecimal())).ToArray());
    }

    [Fact]
    public async Task Categories_SuggestsTheOwnersCategories_FromTransactionsAndBudgets()
    {
        var owner = Guid.NewGuid();
        var client = ClientFor(owner);
        await CreateAsync(client, Budget(category: "Fornecedores", scope: "Business", amount: 5000m, startMonth: "2026-12"));
        var feed = _factory.EventFeed;
        feed.Registered(Guid.NewGuid(), owner, "Personal", 10m, "Alimentação", occurredOn: "2026-12-02");
        feed.Registered(Guid.NewGuid(), owner, "Personal", 10m, "Alimentação", occurredOn: "2026-12-03");
        feed.Registered(Guid.NewGuid(), owner, "Business", 10m, "alimentacao ", occurredOn: "2026-12-04");
        feed.Registered(Guid.NewGuid(), owner, "Business", 10m, "Combustível", occurredOn: "2026-12-05", paymentMethod: "CreditCard");
        feed.Registered(Guid.NewGuid(), owner, "Personal", 10m, "fornecedores", occurredOn: "2026-12-06");
        // Outro usuario: nao aparece.
        feed.Registered(Guid.NewGuid(), Guid.NewGuid(), "Personal", 10m, "Segredo", occurredOn: "2026-12-02");
        await _factory.DrainFeedAsync();

        var categories = await client.GetFromJsonAsync<string[]>("/budgets/categories");

        // "Fornecedores" (orcamento) x "fornecedores" (lancamento) empatam em uso: vale a ordem ordinal.
        Assert.Equal(["Alimentação", "Combustível", "Fornecedores"], categories!);
    }

    [Fact]
    public async Task Categories_ForANewUser_IsEmpty_AndRequiresAToken()
    {
        var categories = await ClientFor(Guid.NewGuid()).GetFromJsonAsync<string[]>("/budgets/categories");
        var anonymous = await _factory.CreateClient().GetAsync("/budgets/categories");

        Assert.Empty(categories!);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Status_IsExceeded_AndGoesBackWhenTheExpenseIsDeleted()
    {
        var owner = Guid.NewGuid();
        var client = ClientFor(owner);
        await CreateAsync(client, Budget(amount: 1000m, startMonth: "2026-12"));
        var big = Guid.NewGuid();
        _factory.EventFeed.Registered(Guid.NewGuid(), owner, "Personal", 600m, "Alimentação", occurredOn: "2026-12-01");
        _factory.EventFeed.Registered(big, owner, "Personal", 700m, "Alimentação", occurredOn: "2026-12-15");
        await _factory.DrainFeedAsync();

        var exceeded = Assert.Single(await StatusAsync(client, "2026-12"));
        Assert.Equal("Exceeded", exceeded.GetProperty("status").GetString());
        Assert.Equal(-300m, exceeded.GetProperty("remaining").GetDecimal());

        _factory.EventFeed.Deleted(big, owner);
        await _factory.DrainFeedAsync();

        var after = Assert.Single(await StatusAsync(client, "2026-12"));
        Assert.Equal(600m, after.GetProperty("spent").GetDecimal());
        Assert.Equal("OnTrack", after.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Status_FollowsTheVersionsAndTheClosing()
    {
        var client = ClientFor(Guid.NewGuid());
        var id = await CreateAsync(client, Budget(amount: 1000m, startMonth: "2026-12"));
        await client.PutAsJsonAsync($"/budgets/{id}/amount", new { amount = 2000m, fromMonth = "2027-01" });
        await client.PostAsJsonAsync($"/budgets/{id}/close", new { fromMonth = "2027-02" });

        Assert.Equal(1000m, Assert.Single(await StatusAsync(client, "2026-12")).GetProperty("limit").GetDecimal());
        Assert.Equal(2000m, Assert.Single(await StatusAsync(client, "2027-01")).GetProperty("limit").GetDecimal());
        Assert.Empty(await StatusAsync(client, "2027-02"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("?month=2026-13")]
    public async Task Status_WithoutAValidMonth_ReturnsValidationProblem(string query)
    {
        var response = await ClientFor(Guid.NewGuid()).GetAsync($"/budgets/status{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("budget.month_invalid", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Status_WithReadScope_IsAllowed_AndWithoutToken_IsUnauthorized()
    {
        var reader = await ClientFor(Guid.NewGuid(), scope: "expenses.read").GetAsync("/budgets/status?month=2026-12");
        var anonymous = await _factory.CreateClient().GetAsync("/budgets/status?month=2026-12");

        Assert.Equal(HttpStatusCode.OK, reader.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}
