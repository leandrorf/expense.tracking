using Expense.Tracking.Accounts.Api.Setup;
using Expense.Tracking.Accounts.Application.Accounts;
using Expense.Tracking.Accounts.Domain.Accounts;
using Expense.Tracking.Accounts.Domain.Common;

namespace Expense.Tracking.Accounts.Api.Endpoints;

public sealed record CreateAccountRequest(string Name, string Scope, string? TaxId, string? Currency);

public sealed record RenameAccountRequest(string Name);

public static class AccountsEndpoints
{
    public static IEndpointRouteBuilder MapAccountsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/accounts").WithTags("Accounts");

        group.MapGet("/", async (HttpContext http, ListAccountsHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await handler.HandleAsync(owner.Value, ct));
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, GetAccountHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(id, owner.Value, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        group.MapPost("/", async (CreateAccountRequest request, HttpContext http, CreateAccountHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            if (!Enum.TryParse<AccountScope>(request.Scope, ignoreCase: true, out var scope))
            {
                return HttpResults.Problem(Error.Validation("account.scope_invalid", "Escopo invalido: use 'Personal' ou 'Business'."));
            }

            var result = await handler.HandleAsync(
                new CreateAccountCommand(owner.Value, request.Name, scope, request.TaxId, request.Currency), ct);

            return result.IsSuccess
                ? Results.Created($"/accounts/{result.Value.Id}", result.Value)
                : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapPut("/{id:guid}", async (Guid id, RenameAccountRequest request, HttpContext http, RenameAccountHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(new RenameAccountCommand(id, owner.Value, request.Name), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapDelete("/{id:guid}", async (Guid id, HttpContext http, DeactivateAccountHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(id, owner.Value, ct);
            return result.IsSuccess ? Results.NoContent() : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        return app;
    }
}
