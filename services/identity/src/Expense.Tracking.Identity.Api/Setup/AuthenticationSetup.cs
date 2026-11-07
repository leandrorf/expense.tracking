using Expense.Tracking.Identity.Application.Abstractions;
using Expense.Tracking.Identity.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Expense.Tracking.Identity.Api.Setup;

public static class AuthenticationSetup
{
    public const string SessionScheme = "Identity.Session";

    public static IServiceCollection AddIdentityAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(options =>
            {
                // Esquema de sessao (cookie) usado pelo endpoint /oauth/authorize.
                options.DefaultScheme = SessionScheme;
            })
            .AddCookie(SessionScheme, options =>
            {
                options.Cookie.Name = SessionScheme;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;

                // API-first: nao redireciona para pagina de login; responde com status codes.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { });

        // Configura a validacao do JWT com a chave publica e issuer/audience (resolvidos do DI).
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<SigningKeyProvider, OAuthOptions>((options, signingKeyProvider, oauthOptions) =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = oauthOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = oauthOptions.DefaultAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKeyProvider.PublicSecurityKey,
                    ValidateLifetime = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub"
                };
            });

        services.AddAuthorization();

        return services;
    }
}
