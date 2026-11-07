using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

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
