using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Expense.Tracking.Reporting.IntegrationTests;

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string SubHeader = "X-Test-Sub";
    public const string ScopeHeader = "X-Test-Scope";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(SubHeader, out var sub) || string.IsNullOrWhiteSpace(sub))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Escopo padrao: so leitura. BDG-2: os testes de escrita mandam o escopo pelo header.
        var scope = Request.Headers.TryGetValue(ScopeHeader, out var requested) ? requested.ToString() : "expenses.read";
        var claims = new[] { new Claim("sub", sub.ToString()), new Claim("scope", scope) };
        var identity = new ClaimsIdentity(claims, SchemeName, "sub", roleType: null);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
