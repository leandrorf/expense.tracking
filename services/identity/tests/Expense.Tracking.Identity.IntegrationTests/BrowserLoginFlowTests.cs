using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Expense.Tracking.Identity.IntegrationTests;

public sealed class BrowserLoginFlowTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string DemoEmail = "demo@expense.local";
    private const string DemoPassword = "Password123!";
    private const string SpaClientId = "expense-tracking-spa";
    private const string RedirectUri = "http://localhost:5173/callback";

    private readonly IdentityApiFactory _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Authorize_WithoutSession_RedirectsToLoginPage()
    {
        var client = CreateClient();

        var response = await client.GetAsync(BuildAuthorizeUrl());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task LoginPage_ReturnsHtmlForm()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/account/login?returnUrl=%2F");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType!.ToString());
        Assert.Contains("name=\"password\"", html);
    }

    [Fact]
    public async Task BrowserFlow_LoginThenAuthorize_IssuesCode()
    {
        var client = CreateClient();
        var (_, challenge) = Pkce.CreateS256();
        var authorizeUrl = BuildAuthorizeUrl(challenge);

        // 1) authorize sem sessao -> redirect para login (com returnUrl)
        var step1 = await client.GetAsync(authorizeUrl);
        Assert.Equal(HttpStatusCode.Redirect, step1.StatusCode);

        // 2) submit do formulario de login (usuario demo semeado)
        var step2 = await client.PostAsync("/account/login/submit", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["email"] = DemoEmail,
                ["password"] = DemoPassword,
                ["returnUrl"] = authorizeUrl
            }));
        Assert.Equal(HttpStatusCode.Redirect, step2.StatusCode);
        Assert.Equal(authorizeUrl, step2.Headers.Location!.OriginalString);

        // 3) segue o returnUrl (agora com cookie) -> redirect para o callback com code
        var step3 = await client.GetAsync(step2.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, step3.StatusCode);
        Assert.StartsWith(RedirectUri, step3.Headers.Location!.OriginalString);
        Assert.Contains("code=", step3.Headers.Location!.Query);
    }

    [Fact]
    public async Task LoginSubmit_WithWrongPassword_ReturnsUnauthorizedHtml()
    {
        var client = CreateClient();

        var response = await client.PostAsync("/account/login/submit", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["email"] = DemoEmail,
                ["password"] = "wrong",
                ["returnUrl"] = "/"
            }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string BuildAuthorizeUrl(string? challenge = null)
    {
        challenge ??= "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
        return $"/oauth/authorize?response_type=code&client_id={SpaClientId}" +
               $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
               $"&scope={Uri.EscapeDataString("openid expenses.read")}" +
               $"&code_challenge={challenge}&code_challenge_method=S256&state=abc";
    }
}
