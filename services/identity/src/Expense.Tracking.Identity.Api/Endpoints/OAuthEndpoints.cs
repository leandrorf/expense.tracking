using System.Security.Claims;
using System.Text;
using Expense.Tracking.Identity.Api.Setup;
using Expense.Tracking.Identity.Application.Authorization;
using Expense.Tracking.Identity.Application.OAuth;
using Expense.Tracking.Identity.Domain.Clients;
using Expense.Tracking.Identity.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.WebUtilities;

namespace Expense.Tracking.Identity.Api.Endpoints;

public static class OAuthEndpoints
{
    public static IEndpointRouteBuilder MapOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/oauth").WithTags("OAuth2");

        // ---- Authorization endpoint (requer sessao de usuario via cookie) ----
        group.MapGet("/authorize", async (
            HttpContext httpContext,
            IssueAuthorizationCodeHandler handler,
            CancellationToken cancellationToken,
            string response_type,
            string client_id,
            string redirect_uri,
            string? scope,
            string? state,
            string? code_challenge,
            string? code_challenge_method) =>
        {
            var userIdValue = httpContext.User.FindFirstValue("sub");
            if (!Guid.TryParse(userIdValue, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(
                new IssueAuthorizationCodeCommand(
                    userId, client_id, response_type, redirect_uri, scope, code_challenge, code_challenge_method),
                cancellationToken);

            if (result.IsFailure)
            {
                // Simplificacao de fundacao: erros sao retornados como JSON (nao redirecionamos
                // para redirect_uri potencialmente nao confiavel). Refinar ao integrar o SPA.
                return HttpResults.OAuthError(result.Error);
            }

            var parameters = new Dictionary<string, string?> { ["code"] = result.Value.Code };
            if (!string.IsNullOrEmpty(state))
            {
                parameters["state"] = state;
            }

            var location = QueryHelpers.AddQueryString(redirect_uri, parameters);
            return Results.Redirect(location);
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthenticationSetup.SessionScheme });

        // ---- Token endpoint (application/x-www-form-urlencoded) ----
        group.MapPost("/token", async (
            HttpContext httpContext,
            AuthorizationCodeGrantHandler authCodeHandler,
            RefreshTokenGrantHandler refreshHandler,
            ClientCredentialsGrantHandler clientCredentialsHandler,
            CancellationToken cancellationToken) =>
        {
            if (!httpContext.Request.HasFormContentType)
            {
                return HttpResults.OAuthError(OAuthErrors.InvalidRequest("Esperado application/x-www-form-urlencoded."));
            }

            var form = await httpContext.Request.ReadFormAsync(cancellationToken);
            var grantType = form["grant_type"].ToString();
            var (clientId, clientSecret) = ResolveClientCredentials(httpContext, form);

            Result<TokenResponse> result = grantType switch
            {
                GrantType.AuthorizationCode => await authCodeHandler.HandleAsync(
                    new AuthorizationCodeGrantCommand(
                        clientId,
                        clientSecret,
                        form["code"].ToString(),
                        form["redirect_uri"].ToString(),
                        form["code_verifier"].ToString()),
                    cancellationToken),

                GrantType.RefreshToken => await refreshHandler.HandleAsync(
                    new RefreshTokenGrantCommand(
                        clientId,
                        clientSecret,
                        form["refresh_token"].ToString(),
                        NullIfEmpty(form["scope"].ToString())),
                    cancellationToken),

                GrantType.ClientCredentials => await clientCredentialsHandler.HandleAsync(
                    new ClientCredentialsGrantCommand(
                        clientId,
                        clientSecret,
                        NullIfEmpty(form["scope"].ToString())),
                    cancellationToken),

                _ => Result.Failure<TokenResponse>(
                    OAuthErrors.UnsupportedGrantType($"grant_type '{grantType}' nao suportado."))
            };

            if (result.IsFailure)
            {
                return HttpResults.OAuthError(result.Error);
            }

            var token = result.Value;
            return Results.Json(new
            {
                access_token = token.AccessToken,
                token_type = token.TokenType,
                expires_in = token.ExpiresIn,
                scope = token.Scope,
                refresh_token = token.RefreshToken
            });
        })
        .DisableAntiforgery();

        return app;
    }

    /// <summary>Extrai client_id/client_secret do header Basic ou do corpo do formulario.</summary>
    private static (string ClientId, string? ClientSecret) ResolveClientCredentials(
        HttpContext httpContext, IFormCollection form)
    {
        var authHeader = httpContext.Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var encoded = authHeader["Basic ".Length..].Trim();
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                var separatorIndex = decoded.IndexOf(':');
                if (separatorIndex > 0)
                {
                    var id = Uri.UnescapeDataString(decoded[..separatorIndex]);
                    var secret = Uri.UnescapeDataString(decoded[(separatorIndex + 1)..]);
                    return (id, secret);
                }
            }
            catch (FormatException)
            {
                // Header malformado: cai no fluxo do corpo.
            }
        }

        return (form["client_id"].ToString(), NullIfEmpty(form["client_secret"].ToString()));
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
