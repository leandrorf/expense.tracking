using System.Security.Claims;
using Expense.Tracking.Identity.Api.Setup;
using Expense.Tracking.Identity.Application.Authentication;
using Expense.Tracking.Identity.Application.Users;
using Microsoft.AspNetCore.Authentication;

namespace Expense.Tracking.Identity.Api.Endpoints;

public sealed record RegisterRequest(string Email, string Password, string? DisplayName);

public sealed record LoginRequest(string Email, string Password);

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account").WithTags("Account");

        group.MapPost("/register", async (
            RegisterRequest request,
            RegisterUserHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(
                new RegisterUserCommand(request.Email, request.Password, request.DisplayName),
                cancellationToken);

            return result.IsSuccess
                ? Results.Created($"/account/{result.Value.UserId}", result.Value)
                : HttpResults.Problem(result.Error);
        });

        group.MapPost("/login", async (
            LoginRequest request,
            AuthenticateUserHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(
                new AuthenticateUserCommand(request.Email, request.Password),
                cancellationToken);

            if (result.IsFailure)
            {
                return HttpResults.Problem(result.Error);
            }

            var user = result.Value;
            await httpContext.SignInAsync(AuthenticationSetup.SessionScheme, CreatePrincipal(user));

            return Results.Ok(new { user.UserId, user.Email, user.DisplayName });
        });

        // Tela de login (HTML) hospedada pelo IdP, usada pelo fluxo de navegador do /oauth/authorize.
        group.MapGet("/login", (HttpContext httpContext, string? returnUrl) =>
        {
            if (httpContext.User.Identity?.IsAuthenticated == true)
            {
                return Results.Redirect(LoginPage.SafeReturnUrl(returnUrl));
            }

            return Results.Content(LoginPage.Render(returnUrl ?? "/"), "text/html; charset=utf-8");
        });

        // Submissao do formulario de login (form-urlencoded) -> sessao por cookie + redirect.
        group.MapPost("/login/submit", async (
            HttpContext httpContext,
            AuthenticateUserHandler handler,
            CancellationToken cancellationToken) =>
        {
            var form = await httpContext.Request.ReadFormAsync(cancellationToken);
            var returnUrl = LoginPage.SafeReturnUrl(form["returnUrl"].ToString());

            var result = await handler.HandleAsync(
                new AuthenticateUserCommand(form["email"].ToString(), form["password"].ToString()),
                cancellationToken);

            if (result.IsFailure)
            {
                return Results.Content(
                    LoginPage.Render(returnUrl, "E-mail ou senha invalidos."),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            await httpContext.SignInAsync(AuthenticationSetup.SessionScheme, CreatePrincipal(result.Value));
            return Results.Redirect(returnUrl);
        }).DisableAntiforgery();

        group.MapPost("/logout", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(AuthenticationSetup.SessionScheme);
            return Results.NoContent();
        });

        group.MapGet("/me", (HttpContext httpContext) =>
        {
            if (httpContext.User.Identity?.IsAuthenticated != true)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(new
            {
                UserId = httpContext.User.FindFirstValue("sub"),
                Name = httpContext.User.Identity?.Name,
                Email = httpContext.User.FindFirstValue(ClaimTypes.Email)
            });
        }).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute
        {
            AuthenticationSchemes = AuthenticationSetup.SessionScheme
        });

        return app;
    }

    private static ClaimsPrincipal CreatePrincipal(AuthenticatedUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new("sub", user.UserId.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email)
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationSetup.SessionScheme));
    }
}
