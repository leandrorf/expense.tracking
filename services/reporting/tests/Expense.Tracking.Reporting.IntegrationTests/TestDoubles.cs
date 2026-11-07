using System.Security.Claims;
using System.Text.Encodings.Web;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Domain.Reports;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Expense.Tracking.Reporting.IntegrationTests;

public sealed class FakeTransactionsClient : ITransactionsClient
{
    public List<TransactionRecord> Records { get; } = [];

    public Task<IReadOnlyList<TransactionRecord>> GetTransactionsAsync(ReportFilter filter, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TransactionRecord>>(Records);
}

public sealed class FakeAccountsClient : IAccountsClient
{
    public Dictionary<Guid, string> Scopes { get; } = [];

    public Task<IReadOnlyDictionary<Guid, string>> GetAccountScopesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(Scopes);
}

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string SubHeader = "X-Test-Sub";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(SubHeader, out var sub) || string.IsNullOrWhiteSpace(sub))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new[] { new Claim("sub", sub.ToString()), new Claim("scope", "expenses.read") };
        var identity = new ClaimsIdentity(claims, SchemeName, "sub", roleType: null);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
