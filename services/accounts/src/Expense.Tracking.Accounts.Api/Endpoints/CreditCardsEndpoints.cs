using Expense.Tracking.Accounts.Api.Setup;
using Expense.Tracking.Accounts.Application.Cards;

namespace Expense.Tracking.Accounts.Api.Endpoints;

public sealed record CreateCreditCardRequest(
    Guid AccountId,
    string Label,
    string? Brand,
    string LastFourDigits,
    decimal CreditLimit,
    int ClosingDay,
    int DueDay);

public static class CreditCardsEndpoints
{
    public static IEndpointRouteBuilder MapCreditCardsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/credit-cards").WithTags("CreditCards");

        group.MapGet("/", async (HttpContext http, Guid? accountId, ListCreditCardsHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await handler.HandleAsync(owner.Value, accountId, ct));
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, GetCreditCardHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(id, owner.Value, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        group.MapPost("/", async (CreateCreditCardRequest request, HttpContext http, CreateCreditCardHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(
                new CreateCreditCardCommand(
                    owner.Value, request.AccountId, request.Label, request.Brand,
                    request.LastFourDigits, request.CreditLimit, request.ClosingDay, request.DueDay),
                ct);

            return result.IsSuccess
                ? Results.Created($"/credit-cards/{result.Value.Id}", result.Value)
                : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapDelete("/{id:guid}", async (Guid id, HttpContext http, DeactivateCreditCardHandler handler, CancellationToken ct) =>
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
