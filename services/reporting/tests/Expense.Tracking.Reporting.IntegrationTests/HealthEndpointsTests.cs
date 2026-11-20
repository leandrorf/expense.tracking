using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Reporting.IntegrationTests;

/// <summary>OBS-3: o reporting segue o contrato padrao de health (referencia: testes do accounts).</summary>
public sealed class HealthEndpointsTests(ReportingApiFactory factory) : IClassFixture<ReportingApiFactory>
{
    private readonly ReportingApiFactory _factory = factory;

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
        // EVT-2: o reporting tem banco proprio (projecao). Continua sem checar outros servicos,
        // para uma queda do transactions nao derrubar a prontidao do reporting.
        var checks = body.GetProperty("checks");
        Assert.Equal("Healthy", checks.GetProperty("database").GetProperty("status").GetString());
        Assert.Single(checks.EnumerateObject());
    }
}
