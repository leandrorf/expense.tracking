using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Expense.Tracking.Transactions.Api.Setup;

public static class AuthenticationSetup
{
    public const string ReadPolicy = "ExpensesRead";
    public const string WritePolicy = "ExpensesWrite";

    public static IServiceCollection AddTransactionsAuthentication(this IServiceCollection services, IConfiguration configuration)
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
                .RequireAssertion(ctx => HasScope(ctx.User, "expenses.write")));

        return services;
    }

    private static bool HasScope(ClaimsPrincipal user, string scope)
    {
        var raw = user.FindFirstValue("scope");
        return !string.IsNullOrEmpty(raw)
            && raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope, StringComparer.Ordinal);
    }
}
