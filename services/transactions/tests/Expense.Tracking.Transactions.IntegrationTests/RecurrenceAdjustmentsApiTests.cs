using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// RJT-4 (ADR-0010, decisoes 3 a 5 e 7): reajuste anual configurado no corpo da regra, aplicado em
/// <c>/transactions/recurrences/{id}/adjustments</c> e sugerido em <c>pendingAdjustment</c>.
/// </summary>
public sealed class RecurrenceAdjustmentsApiTests(TransactionsApiFactory factory) : IClassFixture<TransactionsApiFactory>
{
    private readonly TransactionsApiFactory _factory = factory;

    private static readonly object FivePercentInMarch = new { anniversaryMonth = 3, kind = "Fixed", rate = 5m };

    private HttpClient AuthenticatedAs(string sub, string? scope = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, sub);
        if (scope is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, scope);
        }

        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> TitleAsync(HttpResponseMessage response) => (await Json(response)).GetProperty("title").GetString();

    [Fact]
    public async Task Adjustment_IsConfiguredOnTheRule_AppliedOnce_AndPublished()
    {
        var client = AuthenticatedAs(Guid.NewGuid().ToString());
        var created = await client.PostAsJsonAsync("/transactions/recurrences",
            RecurrencesApiTests.Rule(Guid.NewGuid(), startsOn: "2030-01-10", adjustment: FivePercentInMarch));
        var rule = await Json(created);
        var ruleId = rule.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var adjustment = rule.GetProperty("adjustment");
        Assert.Equal((3, "Fixed", 5m, true), (adjustment.GetProperty("anniversaryMonth").GetInt32(), adjustment.GetProperty("kind").GetString(),
            adjustment.GetProperty("rate").GetDecimal(), adjustment.GetProperty("noReduction").GetBoolean()));

        var applied = await client.PostAsJsonAsync($"/transactions/recurrences/{ruleId}/adjustments", new { year = 2030, rate = 4.5m, version = 1 });
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal([("2030-01", 2000m), ("2030-03", 2090m)], (await Json(applied)).GetProperty("amounts").EnumerateArray()
            .Select(a => (a.GetProperty("fromPeriod").GetString(), a.GetProperty("amount").GetDecimal())));

        var again = await client.PostAsJsonAsync($"/transactions/recurrences/{ruleId}/adjustments", new { year = 2030, amount = 2200m, version = 2 });
        Assert.Equal((HttpStatusCode.Conflict, "recurrence.adjustment_already_applied"), (again.StatusCode, await TitleAsync(again)));

        var service = AuthenticatedAs("expense-tracking-service", "events.read");
        var payload = (await service.GetFromJsonAsync<JsonElement>("/events?after=0&limit=500")).GetProperty("events").EnumerateArray()
            .Last(e => e.GetProperty("aggregateId").GetGuid() == ruleId && e.GetProperty("eventType").GetString() == "RecurrenceSaved")
            .GetProperty("payload");
        Assert.Equal(("Fixed", 5m, 2), (payload.GetProperty("adjustment").GetProperty("kind").GetString(),
            payload.GetProperty("adjustment").GetProperty("estimatedRate").GetDecimal(), payload.GetProperty("amounts").GetArrayLength()));
    }

    [Fact]
    public async Task Adjustment_RefusesInvalidBodies_AndRulesWithoutIt()
    {
        var client = AuthenticatedAs(Guid.NewGuid().ToString());
        var badKind = await client.PostAsJsonAsync("/transactions/recurrences",
            RecurrencesApiTests.Rule(Guid.NewGuid(), adjustment: new { kind = "Monthly" }));
        var badIndex = await client.PostAsJsonAsync("/transactions/recurrences",
            RecurrencesApiTests.Rule(Guid.NewGuid(), adjustment: new { kind = "Index", index = "SELIC" }));
        var plain = await Json(await client.PostAsJsonAsync("/transactions/recurrences", RecurrencesApiTests.Rule(Guid.NewGuid())));
        var notConfigured = await client.PostAsJsonAsync(
            $"/transactions/recurrences/{plain.GetProperty("id").GetGuid()}/adjustments", new { year = 2031, rate = 5m, version = 1 });
        var noYear = await client.PostAsJsonAsync(
            $"/transactions/recurrences/{plain.GetProperty("id").GetGuid()}/adjustments", new { rate = 5m, version = 1 });

        Assert.Equal((HttpStatusCode.BadRequest, "recurrence.adjustment_kind_invalid"), (badKind.StatusCode, await TitleAsync(badKind)));
        Assert.Equal((HttpStatusCode.BadRequest, "recurrence.adjustment_index_invalid"), (badIndex.StatusCode, await TitleAsync(badIndex)));
        Assert.Equal((HttpStatusCode.BadRequest, "recurrence.adjustment_not_configured"), (notConfigured.StatusCode, await TitleAsync(notConfigured)));
        Assert.Equal((HttpStatusCode.BadRequest, "recurrence.adjustment_year_invalid"), (noYear.StatusCode, await TitleAsync(noYear)));
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("adjustment").ValueKind);
    }

    [Fact]
    public async Task PendingAdjustment_AppearsInTheAnniversaryMonth_AndLeavesWhenApplied()
    {
        // Comecou ha pouco mais de um ano, com aniversario neste mes.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = new DateOnly(today.Year - 1, today.Month, 1);
        var client = AuthenticatedAs(Guid.NewGuid().ToString());
        var rule = await Json(await client.PostAsJsonAsync("/transactions/recurrences", RecurrencesApiTests.Rule(Guid.NewGuid(),
            startsOn: start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            adjustment: new { anniversaryMonth = today.Month, kind = "Fixed", rate = 10m })));
        var ruleId = rule.GetProperty("id").GetGuid();

        var pending = rule.GetProperty("pendingAdjustment");
        Assert.Equal((today.Year, today.ToString("yyyy-MM", CultureInfo.InvariantCulture), 2000m, 2200m), (
            pending.GetProperty("year").GetInt32(), pending.GetProperty("period").GetString(),
            pending.GetProperty("currentAmount").GetDecimal(), pending.GetProperty("suggestedAmount").GetDecimal()));

        var applied = await Json(await client.PostAsJsonAsync($"/transactions/recurrences/{ruleId}/adjustments",
            new { year = today.Year, rate = 10m, version = 1 }));
        Assert.Equal(JsonValueKind.Null, applied.GetProperty("pendingAdjustment").ValueKind);
        Assert.Equal(2200m, applied.GetProperty("amount").GetDecimal()); // vigente hoje
    }
}
