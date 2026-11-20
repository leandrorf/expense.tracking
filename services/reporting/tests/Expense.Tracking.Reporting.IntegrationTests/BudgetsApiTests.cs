using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Reporting.IntegrationTests;

/// <summary>
/// BDG-2: definir e listar orcamentos. Os testes dividem o banco; cada um usa um usuario proprio.
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
}
