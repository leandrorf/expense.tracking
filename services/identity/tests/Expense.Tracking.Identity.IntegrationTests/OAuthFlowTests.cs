using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Identity.IntegrationTests;

public sealed class OAuthFlowTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string SpaClientId = "expense-tracking-spa";
    private const string RedirectUri = "http://localhost:5173/callback";
    private const string ServiceClientId = "expense-tracking-service";
    private const string ServiceClientSecret = "dev-service-secret";

    private readonly IdentityApiFactory _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    [Fact]
    public async Task FullAuthorizationCodeFlow_WithPkce_IssuesAndRefreshesTokens()
    {
        var client = CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";

        // 1) Registro
        var userId = await RegisterAsync(client, email);

        // 2) Login (seta cookie de sessao)
        await LoginAsync(client, email);

        // 3) Authorize + PKCE -> authorization code
        var (verifier, challenge) = Pkce.CreateS256();
        var code = await AuthorizeAsync(client, challenge, "openid expenses.read", state: "xyz");

        // 4) Troca code -> tokens
        var tokens = await ExchangeCodeAsync(client, code, verifier);
        var accessToken = tokens.GetProperty("access_token").GetString();
        var refreshToken = tokens.GetProperty("refresh_token").GetString();

        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));
        Assert.Equal("Bearer", tokens.GetProperty("token_type").GetString());

        // 5) UserInfo com o access token
        var sub = await UserInfoSubjectAsync(client, accessToken!);
        Assert.Equal(userId, sub);

        // 6) Refresh token rotaciona
        var refreshed = await RefreshAsync(client, refreshToken!);
        var newRefresh = refreshed.GetProperty("refresh_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(refreshed.GetProperty("access_token").GetString()));
        Assert.NotEqual(refreshToken, newRefresh);

        // 7) Reuso do refresh token antigo deve falhar (foi revogado na rotacao)
        var reusedResponse = await PostTokenAsync(client, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken!,
            ["client_id"] = SpaClientId
        });
        Assert.Equal(HttpStatusCode.BadRequest, reusedResponse.StatusCode);
    }

    [Fact]
    public async Task AuthorizationCode_CannotBeExchangedTwice()
    {
        var client = CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(client, email);
        await LoginAsync(client, email);

        var (verifier, challenge) = Pkce.CreateS256();
        var code = await AuthorizeAsync(client, challenge, "expenses.read", state: null);

        var first = await ExchangeCodeResponseAsync(client, code, verifier);
        var second = await ExchangeCodeResponseAsync(client, code, verifier);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Authorize_WithWrongPkceVerifier_Fails()
    {
        var client = CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(client, email);
        await LoginAsync(client, email);

        var (_, challenge) = Pkce.CreateS256();
        var code = await AuthorizeAsync(client, challenge, "expenses.read", state: null);

        var response = await ExchangeCodeResponseAsync(client, code, "a-completely-different-verifier-value-1234567890");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ClientCredentials_ForConfidentialClient_IssuesAccessTokenWithoutRefresh()
    {
        var client = CreateClient();

        var response = await PostTokenAsync(client, new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = ServiceClientId,
            ["client_secret"] = ServiceClientSecret,
            ["scope"] = "expenses.read"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("access_token").GetString()));
        Assert.Null(body.GetProperty("refresh_token").GetString());
    }

    [Fact]
    public async Task ClientCredentials_WithWrongSecret_ReturnsInvalidClient()
    {
        var client = CreateClient();

        var response = await PostTokenAsync(client, new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = ServiceClientId,
            ["client_secret"] = "wrong-secret",
            ["scope"] = "expenses.read"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_client", body.GetProperty("error").GetString());
    }

    // ---------- helpers ----------

    private static async Task<string> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/account/register", new
        {
            email,
            password = "Password123!",
            displayName = "Test User"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("userId").GetString()!;
    }

    private static async Task LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/account/login", new { email, password = "Password123!" });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> AuthorizeAsync(HttpClient client, string challenge, string scope, string? state)
    {
        var url =
            $"/oauth/authorize?response_type=code&client_id={SpaClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            $"&scope={Uri.EscapeDataString(scope)}" +
            $"&code_challenge={challenge}&code_challenge_method=S256" +
            (state is null ? string.Empty : $"&state={state}");

        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var location = response.Headers.Location!;
        var query = ParseQuery(location.Query);
        Assert.True(query.ContainsKey("code"), "redirect sem parametro 'code'");
        if (state is not null)
        {
            Assert.Equal(state, query["state"]);
        }

        return query["code"];
    }

    private static Task<HttpResponseMessage> ExchangeCodeResponseAsync(HttpClient client, string code, string verifier) =>
        PostTokenAsync(client, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = SpaClientId,
            ["code_verifier"] = verifier
        });

    private static async Task<JsonElement> ExchangeCodeAsync(HttpClient client, string code, string verifier)
    {
        var response = await ExchangeCodeResponseAsync(client, code, verifier);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> RefreshAsync(HttpClient client, string refreshToken)
    {
        var response = await PostTokenAsync(client, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = SpaClientId
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> UserInfoSubjectAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("sub").GetString();
    }

    private static Task<HttpResponseMessage> PostTokenAsync(HttpClient client, Dictionary<string, string> form) =>
        client.PostAsync("/oauth/token", new FormUrlEncodedContent(form));

    private static Dictionary<string, string> ParseQuery(string query)
    {
        return query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
    }
}
