using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Identity.IntegrationTests;

public sealed class DiscoveryEndpointsTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private readonly IdentityApiFactory _factory = factory;

    [Fact]
    public async Task OpenIdConfiguration_ReturnsEndpoints()
    {
        var client = _factory.CreateClient();

        var doc = await client.GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration");

        var issuer = doc.GetProperty("issuer").GetString();
        Assert.False(string.IsNullOrWhiteSpace(issuer));
        // Os endpoints sao derivados da URL da requisicao (nao do issuer), para funcionar
        // atras de rede/proxy; validamos apenas que terminam nos caminhos esperados.
        Assert.EndsWith("/oauth/token", doc.GetProperty("token_endpoint").GetString()!);
        Assert.EndsWith("/oauth/authorize", doc.GetProperty("authorization_endpoint").GetString()!);
        Assert.EndsWith("/.well-known/jwks.json", doc.GetProperty("jwks_uri").GetString()!);
        Assert.Contains("authorization_code", doc.GetProperty("grant_types_supported").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Jwks_ReturnsAtLeastOneRsaKey()
    {
        var client = _factory.CreateClient();

        var doc = await client.GetFromJsonAsync<JsonElement>("/.well-known/jwks.json");
        var keys = doc.GetProperty("keys").EnumerateArray().ToList();

        Assert.NotEmpty(keys);
        Assert.Equal("RSA", keys[0].GetProperty("kty").GetString());
        Assert.Equal("RS256", keys[0].GetProperty("alg").GetString());
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
