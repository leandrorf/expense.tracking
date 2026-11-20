using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Expense.Tracking.Accounts.IntegrationTests;

/// <summary>
/// OBS-3: health padronizado (vivacidade x prontidao) e metricas HTTP no pipeline do
/// OpenTelemetry. O accounts e o servico de referencia; os demais testam o mesmo contrato.
/// </summary>
public sealed class HealthAndMetricsTests(AccountsApiFactory factory) : IClassFixture<AccountsApiFactory>
{
    private readonly AccountsApiFactory _factory = factory;

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
    public async Task Ready_ChecksTheDatabase()
    {
        var response = await _factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty("database").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_Returns503_WhenADependencyFails_ButLiveStaysUp()
    {
        using var failing = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHealthChecks().AddCheck(
                "broken-dependency",
                () => HealthCheckResult.Unhealthy("detalhe interno que nao deve vazar"),
                tags: ["ready"])));
        var client = failing.CreateClient();

        var ready = await client.GetAsync("/health/ready");
        var live = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        var body = await ready.Content.ReadAsStringAsync();
        Assert.Contains("\"broken-dependency\"", body);
        Assert.DoesNotContain("detalhe interno", body);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task HttpServerRequestDuration_IsCollectedByOpenTelemetry()
    {
        var exporter = new MetricNameExporter();
        using var instrumented = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureOpenTelemetryMeterProvider(metrics =>
                metrics.AddReader(new BaseExportingMetricReader(exporter)))));

        var response = await instrumented.CreateClient().GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // O reader manual so exporta no flush; o request ja terminou, entao a medicao existe.
        Assert.True(instrumented.Services.GetRequiredService<MeterProvider>().ForceFlush(5_000));
        Assert.Contains("http.server.request.duration", exporter.Names);
    }

    /// <summary>Guarda so os nomes: o SDK reaproveita os objetos <see cref="Metric"/>.</summary>
    private sealed class MetricNameExporter : BaseExporter<Metric>
    {
        private readonly HashSet<string> _names = [];

        public IReadOnlyCollection<string> Names
        {
            get
            {
                lock (_names)
                {
                    return [.. _names];
                }
            }
        }

        public override ExportResult Export(in Batch<Metric> batch)
        {
            lock (_names)
            {
                foreach (var metric in batch)
                {
                    _names.Add(metric.Name);
                }
            }

            return ExportResult.Success;
        }
    }
}
