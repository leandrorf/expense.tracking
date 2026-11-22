using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Reporting.IntegrationTests;

/// <summary>
/// EVT-3: o resumo le a projecao. A factory nao tem client para o transactions nem para o
/// accounts, entao estes testes tambem provam que o resumo responde com os dois fora do ar.
/// Os testes dividem banco e feed; cada um usa um usuario proprio.
/// </summary>
public sealed class ReportsApiTests(ReportingApiFactory factory) : IClassFixture<ReportingApiFactory>
{
    private readonly ReportingApiFactory _factory = factory;

    private HttpClient ClientFor(Guid owner)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, owner.ToString());
        return client;
    }

    [Fact]
    public async Task Summary_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/reports/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Summary_AggregatesTheOwnersProjection()
    {
        var owner = Guid.NewGuid();
        var feed = _factory.EventFeed;
        feed.Registered(Guid.NewGuid(), owner, accountScope: "Personal", amount: 100m, category: "Alimentacao");
        feed.Registered(Guid.NewGuid(), owner, accountScope: "Business", amount: 400m, category: "Software", paymentMethod: "CreditCard");
        feed.Registered(Guid.NewGuid(), owner, accountScope: "Personal", amount: 1000m, category: "Salario", transactionType: "Income");
        var deleted = Guid.NewGuid();
        feed.Registered(deleted, owner, amount: 999m);
        feed.Deleted(deleted, owner);
        // Lancamento de outro usuario nao entra no resumo.
        feed.Registered(Guid.NewGuid(), Guid.NewGuid(), amount: 5000m);
        await _factory.DrainFeedAsync();

        var summary = await ClientFor(owner).GetFromJsonAsync<JsonElement>("/reports/summary");

        Assert.Equal(500m, summary.GetProperty("totalExpense").GetDecimal());
        Assert.Equal(1000m, summary.GetProperty("totalIncome").GetDecimal());
        Assert.Equal(500m, summary.GetProperty("net").GetDecimal());
        Assert.Equal(3, summary.GetProperty("count").GetInt32());
        Assert.Equal(
            [("Business", 400m), ("Personal", 100m)],
            Totals(summary, "byScope"));
        Assert.Equal(
            [("CreditCard", 400m), ("Pix", 100m)],
            Totals(summary, "byPaymentMethod"));
    }

    [Fact]
    public async Task Summary_LegacyTransactionWithoutScope_IsGroupedAsUnknown()
    {
        var owner = Guid.NewGuid();
        _factory.EventFeed.Registered(Guid.NewGuid(), owner, accountScope: null, amount: 30m);
        await _factory.DrainFeedAsync();

        var summary = await ClientFor(owner).GetFromJsonAsync<JsonElement>("/reports/summary");

        Assert.Equal([("Unknown", 30m)], Totals(summary, "byScope"));
    }

    [Fact]
    public async Task Summary_LegacyTransaction_MovesToItsScope_WhenTheScopeIsResolved()
    {
        // EVT-4: o transactions completa o escopo do lancamento legado com um evento proprio.
        var owner = Guid.NewGuid();
        var legacy = Guid.NewGuid();
        _factory.EventFeed.Registered(legacy, owner, accountScope: null, amount: 30m);
        _factory.EventFeed.ScopeResolved(legacy, owner, "Business");
        await _factory.DrainFeedAsync();

        var summary = await ClientFor(owner).GetFromJsonAsync<JsonElement>("/reports/summary");

        Assert.Equal([("Business", 30m)], Totals(summary, "byScope"));
    }

    [Fact]
    public async Task Summary_FiltersByAccountAndInclusivePeriod()
    {
        var owner = Guid.NewGuid();
        var account = Guid.NewGuid();
        var feed = _factory.EventFeed;
        feed.Registered(Guid.NewGuid(), owner, accountId: account, amount: 1m, occurredOn: "2026-10-31");
        feed.Registered(Guid.NewGuid(), owner, accountId: account, amount: 10m, occurredOn: "2026-11-01");
        feed.Registered(Guid.NewGuid(), owner, accountId: account, amount: 20m, occurredOn: "2026-11-30");
        feed.Registered(Guid.NewGuid(), owner, accountId: account, amount: 300m, occurredOn: "2026-12-01");
        feed.Registered(Guid.NewGuid(), owner, amount: 4000m, occurredOn: "2026-11-15");
        await _factory.DrainFeedAsync();

        var summary = await ClientFor(owner).GetFromJsonAsync<JsonElement>(
            $"/reports/summary?accountId={account}&from=2026-11-01&to=2026-11-30");

        Assert.Equal(30m, summary.GetProperty("totalExpense").GetDecimal());
        Assert.Equal(2, summary.GetProperty("count").GetInt32());
    }

    private static (string Key, decimal Total)[] Totals(JsonElement summary, string property) =>
        summary.GetProperty(property).EnumerateArray()
            .Select(t => (t.GetProperty("key").GetString()!, t.GetProperty("total").GetDecimal()))
            .ToArray();

    [Fact]
    public async Task Summary_FollowsAnEdit_AndAnEditAfterTheDeletionIsIgnored()
    {
        // LNC-4 (ADR-0007): o Resumo le a projecao; a edicao troca valor, categoria e tipo.
        var owner = Guid.NewGuid();
        var feed = _factory.EventFeed;
        var (edited, deleted) = (Guid.NewGuid(), Guid.NewGuid());
        feed.Registered(edited, owner, amount: 100m, category: "Alimentacao");
        feed.Updated(edited, owner, 250m, "2026-11-19", category: "Feira");
        feed.Registered(deleted, owner, amount: 70m);
        feed.Deleted(deleted, owner);
        feed.Updated(deleted, owner, 999m, "2026-11-19");
        await _factory.DrainFeedAsync();

        var summary = await ClientFor(owner).GetFromJsonAsync<JsonElement>("/reports/summary");

        Assert.Equal((250m, 0m, 1), (summary.GetProperty("totalExpense").GetDecimal(), summary.GetProperty("totalIncome").GetDecimal(),
            summary.GetProperty("count").GetInt32()));
        Assert.Equal("Feira", Assert.Single(summary.GetProperty("byCategory").EnumerateArray()).GetProperty("key").GetString());
    }
}
