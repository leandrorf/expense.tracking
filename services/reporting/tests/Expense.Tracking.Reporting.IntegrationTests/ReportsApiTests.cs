using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Expense.Tracking.Reporting.Domain.Reports;

namespace Expense.Tracking.Reporting.IntegrationTests;

public sealed class ReportsApiTests(ReportingApiFactory factory) : IClassFixture<ReportingApiFactory>
{
    private readonly ReportingApiFactory _factory = factory;

    private HttpClient AuthedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, Guid.NewGuid().ToString());
        return client;
    }

    [Fact]
    public async Task Summary_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/reports/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Summary_AggregatesTransactionsFromDownstream()
    {
        var pf = Guid.NewGuid();
        var pj = Guid.NewGuid();
        _factory.Accounts.Scopes[pf] = "Personal";
        _factory.Accounts.Scopes[pj] = "Business";
        _factory.Transactions.Records.Clear();
        _factory.Transactions.Records.AddRange(
        [
            new TransactionRecord(pf, "Expense", "Pix", "Alimentacao", 100m, "BRL"),
            new TransactionRecord(pj, "Expense", "CreditCard", "Software", 400m, "BRL"),
            new TransactionRecord(pf, "Income", "BankTransfer", "Salario", 1000m, "BRL"),
        ]);

        var summary = await AuthedClient().GetFromJsonAsync<JsonElement>("/reports/summary");

        Assert.Equal(500m, summary.GetProperty("totalExpense").GetDecimal());
        Assert.Equal(1000m, summary.GetProperty("totalIncome").GetDecimal());
        Assert.Equal(500m, summary.GetProperty("net").GetDecimal());
        Assert.Equal(3, summary.GetProperty("count").GetInt32());

        var byScope = summary.GetProperty("byScope").EnumerateArray().ToArray();
        Assert.Equal("Business", byScope[0].GetProperty("key").GetString());
        Assert.Equal(400m, byScope[0].GetProperty("total").GetDecimal());
    }
}
