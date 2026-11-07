using System.Security.Claims;
using Expense.Tracking.Identity.Application.Abstractions;
using Expense.Tracking.Identity.Domain.Authorization;
using Expense.Tracking.Identity.Domain.Clients;
using Expense.Tracking.Identity.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Expense.Tracking.Identity.Api.Endpoints;

public static class DiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        // Metadados do servidor de autorizacao (RFC 8414 / OpenID Connect Discovery).
        app.MapGet("/.well-known/openid-configuration", (OAuthOptions options) =>
        {
            var issuer = options.Issuer.TrimEnd('/');
            return Results.Json(new
            {
                issuer,
                authorization_endpoint = $"{issuer}/oauth/authorize",
                token_endpoint = $"{issuer}/oauth/token",
                jwks_uri = $"{issuer}/.well-known/jwks.json",
                userinfo_endpoint = $"{issuer}/connect/userinfo",
                response_types_supported = new[] { "code" },
                grant_types_supported = new[]
                {
                    GrantType.AuthorizationCode,
                    GrantType.RefreshToken,
                    GrantType.ClientCredentials
                },
                code_challenge_methods_supported = new[]
                {
                    CodeChallengeMethod.S256,
                    CodeChallengeMethod.Plain
                },
                token_endpoint_auth_methods_supported = new[] { "client_secret_basic", "client_secret_post", "none" },
                subject_types_supported = new[] { "public" },
                id_token_signing_alg_values_supported = new[] { "RS256" }
            });
        }).WithTags("Discovery");

        // Conjunto de chaves publicas (JWKS) para validacao das assinaturas.
        app.MapGet("/.well-known/jwks.json", (SigningKeyProvider signingKeyProvider) =>
        {
            var jwk = signingKeyProvider.GetPublicJsonWebKey();
            return Results.Json(new
            {
                keys = new[]
                {
                    new
                    {
                        kty = jwk.Kty,
                        use = jwk.Use,
                        kid = jwk.Kid,
                        alg = jwk.Alg,
                        n = jwk.N,
                        e = jwk.E
                    }
                }
            });
        }).WithTags("Discovery");

        // UserInfo (protegido por Bearer).
        app.MapGet("/connect/userinfo", (ClaimsPrincipal user) =>
        {
            return Results.Json(new
            {
                sub = user.FindFirstValue("sub"),
                client_id = user.FindFirstValue("client_id"),
                scope = user.FindFirstValue("scope")
            });
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
        .WithTags("OAuth2");

        return app;
    }
}
