using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Expense.Tracking.Accounts.Api.Setup;

public static class AuthenticationSetup
{
    public const string ReadPolicy = "ExpensesRead";
    public const string WritePolicy = "ExpensesWrite";
    public const string AccountScopesReadPolicy = "AccountScopesRead";
    public const string AccountScopesReadScope = "accounts.scopes.read";

    /// <summary>NTF-7: cartoes de um dono para servicos (o job de avisos do reporting).</summary>
    public const string AccountCardsReadPolicy = "AccountCardsRead";
    public const string AccountCardsReadScope = "accounts.cards.read";

    public static IServiceCollection AddAccountsAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var authority = configuration["Jwt:Authority"] ?? "http://localhost:5080";
        var audience = configuration["Jwt:Audience"] ?? "expense-tracking-api";
        var metadataAddress = configuration["Jwt:MetadataAddress"];
        if (string.IsNullOrWhiteSpace(metadataAddress))
        {
            metadataAddress = $"{authority.TrimEnd('/')}/.well-known/openid-configuration";
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // O issuer dos tokens e a URL publica; os metadados podem ser buscados numa
                // URL interna (ex.: http://identity:8080) dentro da rede Docker.
                options.MetadataAddress = metadataAddress;
                options.RequireHttpsMetadata = false;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    NameClaimType = "sub",
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(ReadPolicy, p => p.RequireAuthenticatedUser()
                .RequireAssertion(ctx => HasScope(ctx.User, "expenses.read") || HasScope(ctx.User, "expenses.write")))
            .AddPolicy(WritePolicy, p => p.RequireAuthenticatedUser()
                .RequireAssertion(ctx => HasScope(ctx.User, "expenses.write")))
            // EVT-4: endpoint interno, so para tokens de servico (client credentials).
            .AddPolicy(AccountScopesReadPolicy, p => p.RequireAuthenticatedUser()
                .RequireAssertion(ctx => HasScope(ctx.User, AccountScopesReadScope)))
            .AddPolicy(AccountCardsReadPolicy, p => p.RequireAuthenticatedUser()
                .RequireAssertion(ctx => HasScope(ctx.User, AccountCardsReadScope)));

        return services;
    }

    private static bool HasScope(ClaimsPrincipal user, string scope)
    {
        var raw = user.FindFirstValue("scope");
        return !string.IsNullOrEmpty(raw)
            && raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope, StringComparer.Ordinal);
    }
}
