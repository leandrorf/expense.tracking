using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Expense.Tracking.Identity.Api.Setup;
using Expense.Tracking.Identity.Domain.Clients;

namespace Expense.Tracking.Identity.IntegrationTests;

/// <summary>
/// IDX-2 (ADR-0003): clientes OAuth, usuario demo, CORS e dica da tela de login vem so da
/// configuracao de quem roda o Identity, sem padrao no codigo.
/// </summary>
public sealed class SeedConfigurationTests
{
    private static Task<HttpResponseMessage> ServiceTokenAsync(HttpClient client, string scope) =>
        client.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = IdentityApiFactory.ServiceClientId,
            ["client_secret"] = IdentityApiFactory.ServiceClientSecret,
            ["scope"] = scope,
        }));

    private static Dictionary<string, string?> Without(Func<string, bool> drop)
    {
        var settings = IdentityApiFactory.DefaultSettings();
        foreach (var key in settings.Keys.Where(drop).ToList())
        {
            settings.Remove(key);
        }

        return settings;
    }

    [Fact]
    public async Task WithoutSeedConfiguration_ThereAreNoClientsUsersCorsOrHint()
    {
        using var factory = new IdentityApiFactory(Without(k => k != "Seed:Enabled"));
        var client = factory.CreateClient();

        var token = await ServiceTokenAsync(client, "expenses.read");
        var login = await client.PostAsJsonAsync("/account/login",
            new { email = IdentityApiFactory.DemoEmail, password = IdentityApiFactory.DemoPassword });
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/oauth/token");
        preflight.Headers.Add("Origin", IdentityApiFactory.SpaOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        var cors = await client.SendAsync(preflight);
        var page = await client.GetStringAsync("/account/login?returnUrl=%2F");

        Assert.Equal(HttpStatusCode.Unauthorized, token.StatusCode);
        Assert.Equal("invalid_client", (await token.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.False(cors.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.DoesNotContain("class=\"hint\"", page);
    }

    [Fact]
    public async Task ConfiguredCorsOriginAndHint_AreApplied()
    {
        using var factory = new IdentityApiFactory();
        var client = factory.CreateClient();

        var preflight = new HttpRequestMessage(HttpMethod.Options, "/oauth/token");
        preflight.Headers.Add("Origin", IdentityApiFactory.SpaOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        var cors = await client.SendAsync(preflight);
        var page = await client.GetStringAsync("/account/login?returnUrl=%2F");

        Assert.Equal(IdentityApiFactory.SpaOrigin, Assert.Single(cors.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains($"<p class=\"hint\">{System.Net.WebUtility.HtmlEncode(IdentityApiFactory.LoginHint)}</p>", page);
    }

    [Fact]
    public async Task NewScopeInConfiguration_IsGrantedToAnExistingClient_WithoutRecreatingIt()
    {
        var database = $"identity-reconcile-{Guid.NewGuid():N}";
        var before = Without(k => k == $"Seed:Clients:{IdentityApiFactory.ServiceClientId}:Scopes:3");
        using (var first = new IdentityApiFactory(before, database))
        {
            var denied = await ServiceTokenAsync(first.CreateClient(), "accounts.scopes.read");
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }

        // "Reinicia" sobre o mesmo banco com o escopo novo na configuracao.
        using var second = new IdentityApiFactory(IdentityApiFactory.DefaultSettings(), database);
        var granted = await ServiceTokenAsync(second.CreateClient(), "accounts.scopes.read");

        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
    }

    [Fact]
    public void InvalidConfiguration_FailsAtStartup_WithoutLeakingSecrets()
    {
        var settings = IdentityApiFactory.DefaultSettings();
        settings[$"Seed:Clients:{IdentityApiFactory.ServiceClientId}:Secret"] = null;
        settings[$"Seed:Clients:{IdentityApiFactory.SpaClientId}:GrantTypes:1"] = "implicit";
        settings["Seed:Users:demo:Email"] = null;
        using var factory = new IdentityApiFactory(settings);

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains($"Seed:Clients:{IdentityApiFactory.ServiceClientId}:Secret", error.Message);
        Assert.Contains($"Seed:Clients:{IdentityApiFactory.SpaClientId}:GrantTypes: grant 'implicit'", error.Message);
        Assert.Contains("Seed:Users:demo:Email", error.Message);
        Assert.DoesNotContain(IdentityApiFactory.DemoPassword, error.Message);
    }

    [Fact]
    public void Validate_RejectsPublicClientWithSecret_AndAuthorizationCodeWithoutRedirect()
    {
        var options = new SeedOptions
        {
            Clients =
            {
                ["spa"] = new SeedClientOptions
                {
                    Type = ClientType.Public,
                    Secret = "nao-deveria",
                    GrantTypes = [GrantType.AuthorizationCode],
                },
            },
            Users = { ["demo"] = new SeedUserOptions { Email = "a@b.com" } },
        };

        var errors = options.Validate();

        Assert.Equal(
            [
                "Seed:Clients:spa:RedirectUris: authorization_code exige ao menos um redirect URI.",
                "Seed:Clients:spa:Secret: cliente publico nao pode ter segredo.",
                "Seed:Users:demo:Password: obrigatoria.",
            ],
            errors);
        Assert.DoesNotContain(errors, e => e.Contains("nao-deveria", StringComparison.Ordinal));
    }
}
