using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>OBS-3: o transactions segue o contrato padrao de health (referencia: testes do accounts).</summary>
public sealed class HealthEndpointsTests(TransactionsApiFactory factory) : IClassFixture<TransactionsApiFactory>
{
    private readonly TransactionsApiFactory _factory = factory;

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    public async Task Live_IsHealthy_AndRunsNoChecks(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Empty(body.GetProperty("checks").EnumerateObject());
    }

    [Fact]
    public async Task Ready_IsHealthy_AndChecksTheDatabase()
    {
        var response = await _factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty("database").GetProperty("status").GetString());
    }
}
