using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Reporting.IntegrationTests;

/// <summary>
/// MET-5 (ADR-0013, regras 6 e 9): objetivos e aportes pela API. Hoje = 2026-11-21 (relogio do
/// <see cref="InvoicesApiFactory"/>).
/// </summary>
public sealed class SavingsObjectivesApiTests(InvoicesApiFactory factory) : IClassFixture<InvoicesApiFactory>
{
    private readonly InvoicesApiFactory _factory = factory;

    private HttpClient ClientFor(Guid owner, string scope = "expenses.read expenses.write")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, owner.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, scope);
        return client;
    }

    private static object Objective(string? scope = "personal", string? name = "Reserva de emergência", decimal target = 30000m, string? deadline = "2027-12") =>
        new { scope, name, target, deadline };

    private static async Task<string> ProblemCode(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()!;

    private static async Task<JsonElement> Created(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/goals/objectives", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Create_ThenContribute_ShowsTheSavedAmount_OnlyToTheOwner_AndOutsideTheForecast()
    {
        var owner = Guid.NewGuid();
        var client = ClientFor(owner);

        var objective = await Created(client, Objective());
        var id = objective.GetProperty("id").GetGuid();
        var contributed = await client.PostAsJsonAsync($"/goals/objectives/{id}/contributions", new { date = "2026-11-21", amount = 2000m, note = "Décimo terceiro" });

        Assert.Equal(HttpStatusCode.OK, contributed.StatusCode);
        var list = await client.GetFromJsonAsync<JsonElement[]>("/goals/objectives");
        var item = Assert.Single(list!);
        Assert.Equal(("personal", "Reserva de emergência", 30000m, "2027-12", false, 2000m),
            (item.GetProperty("scope").GetString(), item.GetProperty("name").GetString(), item.GetProperty("target").GetDecimal(),
                item.GetProperty("deadline").GetString(), item.GetProperty("archived").GetBoolean(), item.GetProperty("saved").GetDecimal()));
        var contribution = Assert.Single(item.GetProperty("contributions").EnumerateArray());
        Assert.Equal(("2026-11-21", 2000m, "Décimo terceiro"),
            (contribution.GetProperty("date").GetString(), contribution.GetProperty("amount").GetDecimal(), contribution.GetProperty("note").GetString()));

        // Aporte nao e lancamento: a previsao (e o resultado do mes) nao muda.
        var forecast = await client.GetFromJsonAsync<JsonElement>("/reports/cash-forecast?months=1");
        Assert.Equal(0m, forecast.GetProperty("months")[0].GetProperty("personal").GetProperty("result").GetDecimal());

        var stranger = ClientFor(Guid.NewGuid());
        Assert.Empty((await stranger.GetFromJsonAsync<JsonElement[]>("/goals/objectives"))!);
        var foreign = await stranger.PostAsJsonAsync($"/goals/objectives/{id}/contributions", new { date = "2026-11-21", amount = 1m });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("goal.not_found", await ProblemCode(foreign));
    }

    [Fact]
    public async Task Withdrawal_BeyondTheSaved_AndDeletingTheDeposit_AreRejected()
    {
        var client = ClientFor(Guid.NewGuid());
        var id = (await Created(client, Objective(deadline: null))).GetProperty("id").GetGuid();
        var afterDeposit = await (await client.PostAsJsonAsync($"/goals/objectives/{id}/contributions", new { date = "2026-11-01", amount = 2000m }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var deposit = afterDeposit.GetProperty("contributions")[0].GetProperty("id").GetGuid();

        var tooMuch = await client.PostAsJsonAsync($"/goals/objectives/{id}/contributions", new { date = "2026-11-21", amount = -2500m });
        var withdrawal = await client.PostAsJsonAsync($"/goals/objectives/{id}/contributions", new { date = "2026-11-21", amount = -500m });
        var removeDeposit = await client.DeleteAsync($"/goals/objectives/{id}/contributions/{deposit}");
        var removeMissing = await client.DeleteAsync($"/goals/objectives/{id}/contributions/{Guid.NewGuid()}");

        Assert.Equal("goal.balance_negative", await ProblemCode(tooMuch));
        Assert.Equal(1500m, (await withdrawal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("saved").GetDecimal());
        Assert.Equal("goal.balance_negative", await ProblemCode(removeDeposit));
        Assert.Equal(HttpStatusCode.NotFound, removeMissing.StatusCode);
        Assert.Equal("goal.contribution_not_found", await ProblemCode(removeMissing));
    }

    [Fact]
    public async Task Edit_Archive_AndUnarchive()
    {
        var client = ClientFor(Guid.NewGuid());
        var id = (await Created(client, Objective())).GetProperty("id").GetGuid();

        var edited = await client.PutAsJsonAsync($"/goals/objectives/{id}", new { name = "Reserva", target = 25000m, deadline = (string?)null });
        var archived = await client.PostAsync($"/goals/objectives/{id}/archive", null);
        var whileArchived = await client.PostAsJsonAsync($"/goals/objectives/{id}/contributions", new { date = "2026-11-21", amount = 10m });
        var active = await client.GetFromJsonAsync<JsonElement[]>("/goals/objectives");
        var all = await client.GetFromJsonAsync<JsonElement[]>("/goals/objectives?includeArchived=true");
        var reopened = await client.PostAsync($"/goals/objectives/{id}/unarchive", null);

        var body = await edited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("Reserva", 25000m, JsonValueKind.Null),
            (body.GetProperty("name").GetString(), body.GetProperty("target").GetDecimal(), body.GetProperty("deadline").ValueKind));
        Assert.True((await archived.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("archived").GetBoolean());
        Assert.Equal("goal.archived", await ProblemCode(whileArchived));
        Assert.Empty(active!);
        Assert.Single(all!);
        Assert.False((await reopened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("archived").GetBoolean());
    }

    public static TheoryData<object, string> InvalidObjectives() => new()
    {
        { Objective(name: ""), "goal.name_invalid" },
        { Objective(name: new string('a', 61)), "goal.name_invalid" },
        { Objective(target: 0m), "goal.target_invalid" },
        { Objective(target: 10.555m), "goal.target_invalid" },
        { Objective(deadline: "2026-10"), "goal.deadline_invalid" },
        { Objective(deadline: "dez/2027"), "goal.deadline_invalid" },
        { Objective(scope: "familia"), "goal.scope_invalid" },
        { Objective(scope: null), "goal.scope_invalid" },
    };

    [Theory]
    [MemberData(nameof(InvalidObjectives))]
    public async Task Create_WithInvalidValues_Returns400WithTheCode(object body, string code)
    {
        var response = await ClientFor(Guid.NewGuid()).PostAsJsonAsync("/goals/objectives", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await ProblemCode(response));
    }

    [Theory]
    [InlineData("2026-11-22", 10, "goal.contribution_in_future")]
    [InlineData("2026-11-21", 0, "goal.contribution_invalid")]
    [InlineData("21/11/2026", 10, "goal.contribution_invalid")]
    public async Task Contribution_WithInvalidValues_Returns400WithTheCode(string date, int amount, string code)
    {
        var client = ClientFor(Guid.NewGuid());
        var id = (await Created(client, Objective())).GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync($"/goals/objectives/{id}/contributions", new { date, amount });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await ProblemCode(response));
    }

    [Fact]
    public async Task Writing_NeedsTheWriteScope()
    {
        var response = await ClientFor(Guid.NewGuid(), "expenses.read").PostAsJsonAsync("/goals/objectives", Objective());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
