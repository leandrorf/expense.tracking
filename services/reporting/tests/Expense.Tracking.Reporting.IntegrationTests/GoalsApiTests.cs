using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Reporting.IntegrationTests;

/// <summary>
/// MET-2 (ADR-0013, regras 3 e 9): definir, mudar e encerrar a meta mensal por escopo. Os testes dividem o
/// banco; cada um usa um usuario proprio.
/// </summary>
public sealed class GoalsApiTests(ReportingApiFactory factory) : IClassFixture<ReportingApiFactory>
{
    private readonly ReportingApiFactory _factory = factory;

    private HttpClient ClientFor(Guid owner, string scope = "expenses.read expenses.write")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, owner.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, scope);
        return client;
    }

    private static object Goal(string? kind = "SaveAmount", decimal? amount = 1000m, int? percent = null, string? fromMonth = "2026-12") =>
        new { kind, amount, percent, fromMonth };

    private static async Task<string> ProblemCode(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()!;

    [Fact]
    public async Task Put_CreatesTheGoal_ThenChangesItFromAMonth_KeepingTheEarlierTerm()
    {
        var client = ClientFor(Guid.NewGuid());

        var created = await client.PutAsJsonAsync("/goals/monthly/personal", Goal());
        var changed = await client.PutAsJsonAsync("/goals/monthly/personal", Goal(kind: "SavePercent", amount: null, percent: 20, fromMonth: "2027-03"));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/goals/monthly/personal", created.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var list = await client.GetFromJsonAsync<JsonElement[]>("/goals/monthly");
        var goal = Assert.Single(list!);
        Assert.Equal("personal", goal.GetProperty("scope").GetString());
        Assert.Equal("2026-12", goal.GetProperty("startMonth").GetString());
        Assert.Equal(JsonValueKind.Null, goal.GetProperty("closedFromMonth").ValueKind);
        Assert.Equal(
            [("2026-12", "SaveAmount", "1000", "null"), ("2027-03", "SavePercent", "null", "20")],
            goal.GetProperty("terms").EnumerateArray()
                .Select(t => (t.GetProperty("fromMonth").GetString(), t.GetProperty("kind").GetString(),
                    t.GetProperty("amount").GetRawText(),
                    t.GetProperty("percent").GetRawText()))
                .ToArray());
    }

    [Fact]
    public async Task EachScope_HasItsOwnGoal_VisibleOnlyToItsOwner()
    {
        var client = ClientFor(Guid.NewGuid());

        await client.PutAsJsonAsync("/goals/monthly/personal", Goal());
        await client.PutAsJsonAsync("/goals/monthly/business", Goal(kind: "SpendCeiling", amount: 6000m));

        var list = await client.GetFromJsonAsync<JsonElement[]>("/goals/monthly");
        Assert.Equal(["personal", "business"], list!.Select(g => g.GetProperty("scope").GetString()!).ToArray());
        Assert.Empty((await ClientFor(Guid.NewGuid()).GetFromJsonAsync<JsonElement[]>("/goals/monthly"))!);
    }

    [Fact]
    public async Task Delete_ClosesFromAMonth_AndANewGoalMustStartFromThere()
    {
        var client = ClientFor(Guid.NewGuid());
        await client.PutAsJsonAsync("/goals/monthly/personal", Goal());

        var closed = await client.DeleteAsync("/goals/monthly/personal?fromMonth=2027-03");
        var again = await client.DeleteAsync("/goals/monthly/personal?fromMonth=2027-04");
        var overlapping = await client.PutAsJsonAsync("/goals/monthly/personal", Goal(fromMonth: "2027-02"));
        var next = await client.PutAsJsonAsync("/goals/monthly/personal", Goal(amount: 1500m, fromMonth: "2027-03"));

        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal("2027-03", (await closed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("closedFromMonth").GetString());
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("goal.not_found", await ProblemCode(again));
        Assert.Equal(HttpStatusCode.BadRequest, overlapping.StatusCode);
        Assert.Equal("goal.month_before_start", await ProblemCode(overlapping));
        Assert.Equal(HttpStatusCode.Created, next.StatusCode);
        Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement[]>("/goals/monthly"))!.Length);
    }

    public static TheoryData<string, object, string> InvalidRequests() => new()
    {
        { "personal", Goal(amount: -10m), "goal.amount_invalid" },
        { "personal", Goal(amount: 10.555m), "goal.amount_invalid" },
        { "personal", Goal(amount: null), "goal.amount_invalid" },
        { "personal", Goal(kind: "SavePercent", amount: null, percent: 101), "goal.percent_invalid" },
        { "personal", Goal(kind: "SavePercent", amount: null, percent: null), "goal.percent_invalid" },
        { "personal", Goal(kind: "Guardar"), "goal.kind_invalid" },
        { "personal", Goal(kind: "1"), "goal.kind_invalid" },
        { "personal", Goal(kind: null), "goal.kind_invalid" },
        { "personal", Goal(fromMonth: "2026-13"), "goal.month_invalid" },
        { "personal", Goal(fromMonth: null), "goal.month_invalid" },
        { "familia", Goal(), "goal.scope_invalid" },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Put_WithInvalidValues_Returns400WithTheCode(string scope, object body, string code)
    {
        var response = await ClientFor(Guid.NewGuid()).PutAsJsonAsync($"/goals/monthly/{scope}", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await ProblemCode(response));
    }

    [Fact]
    public async Task Change_BeforeTheStart_AndClose_OnTheStart_AreRejected()
    {
        var client = ClientFor(Guid.NewGuid());
        await client.PutAsJsonAsync("/goals/monthly/personal", Goal());

        var before = await client.PutAsJsonAsync("/goals/monthly/personal", Goal(fromMonth: "2026-11"));
        var closeOnStart = await client.DeleteAsync("/goals/monthly/personal?fromMonth=2026-12");
        var badMonth = await client.DeleteAsync("/goals/monthly/personal?fromMonth=dez");

        Assert.Equal("goal.month_before_start", await ProblemCode(before));
        Assert.Equal("goal.month_before_start", await ProblemCode(closeOnStart));
        Assert.Equal("goal.month_invalid", await ProblemCode(badMonth));
    }

    [Fact]
    public async Task Writing_NeedsTheWriteScope()
    {
        var readOnly = ClientFor(Guid.NewGuid(), "expenses.read");

        var put = await readOnly.PutAsJsonAsync("/goals/monthly/personal", Goal());
        var get = await readOnly.GetAsync("/goals/monthly");

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }
}
