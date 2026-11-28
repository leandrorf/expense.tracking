using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Configuration;

namespace Expense.Tracking.Gateway.IntegrationTests;

public sealed class GatewayRoutingTests(GatewayRoutingTests.Factory factory) : IClassFixture<GatewayRoutingTests.Factory>
{
    private readonly Factory _factory = factory;

    [Fact]
    public async Task Health_IsPublic()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApiRoute_WithoutToken_ReturnsUnauthorized()
    {
        // A borda valida o token ANTES de encaminhar; sem token nao ha proxy.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/accounts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/internal/account-scopes")]
    [InlineData("/api/internal/qualquer/coisa")]
    public async Task InternalServiceEndpoints_AreNotExposed(string path)
    {
        // EVT-4: sem o bloqueio, o catch-all /api/** levaria ao accounts (e responderia 401 aqui).
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void ImportsRoute_GoesToTransactions_BeforeTheAccountsCatchAll()
    {
        // IMP-4: sem esta rota, /api/imports cairia no catch-all /api/** do accounts.
        var config = _factory.Services.GetRequiredService<IProxyConfigProvider>().GetConfig();

        var imports = Assert.Single(config.Routes, r => r.RouteId == "imports-api");
        var catchAll = Assert.Single(config.Routes, r => r.RouteId == "accounts-api");
        Assert.Equal("/api/imports/{**catch-all}", imports.Match.Path);
        Assert.Equal("transactions", imports.ClusterId);
        Assert.Equal("authenticated", imports.AuthorizationPolicy);
        Assert.True(imports.Order < catchAll.Order);
    }

    [Fact]
    public void GoalsRoute_GoesToReporting_BeforeTheAccountsCatchAll()
    {
        // MET-2 (ADR-0013, regra 9): sem esta rota, /api/goals cairia no catch-all /api/** do accounts.
        var config = _factory.Services.GetRequiredService<IProxyConfigProvider>().GetConfig();

        var goals = Assert.Single(config.Routes, r => r.RouteId == "goals-api");
        var catchAll = Assert.Single(config.Routes, r => r.RouteId == "accounts-api");
        Assert.Equal("/api/goals/{**catch-all}", goals.Match.Path);
        Assert.Equal("reporting", goals.ClusterId);
        Assert.Equal("authenticated", goals.AuthorizationPolicy);
        Assert.True(goals.Order < catchAll.Order);
    }

    [Fact]
    public async Task ImportsRoute_WithoutToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/api/imports", new StringContent("x"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ImportsRoute_BodyAboveTheLimit_IsRejectedAtTheEdge()
    {
        // Recusado antes de autenticar e de repassar: o arquivo grande nao chega ao transactions.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var content = new ByteArrayContent(new byte[ImportsLimits.MaxRequestBytes + 1]);

        var response = await client.PostAsync("/api/imports", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("import.file_too_large", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownRoute_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/nao-existe");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment(Environments.Development);
    }
}
