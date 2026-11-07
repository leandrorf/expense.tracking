using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Expense.Tracking.Accounts.IntegrationTests;

/// <summary>
/// Esquema de autenticacao de teste: autentica se o header 'X-Test-Sub' estiver presente,
/// com os escopos do header 'X-Test-Scope' (default read+write). Sem o header -> 401.
/// Isola os testes do accounts da dependencia real do Identity.
/// </summary>
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

        var scope = Request.Headers.TryGetValue(ScopeHeader, out var s) && !string.IsNullOrWhiteSpace(s)
            ? s.ToString()
            : "expenses.read expenses.write";

        var claims = new[]
        {
            new Claim("sub", sub.ToString()),
            new Claim("scope", scope),
        };

        var identity = new ClaimsIdentity(claims, SchemeName, "sub", roleType: null);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
