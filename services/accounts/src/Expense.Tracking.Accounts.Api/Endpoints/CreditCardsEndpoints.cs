using Expense.Tracking.Accounts.Api.Setup;
using Expense.Tracking.Accounts.Application.Cards;
using Expense.Tracking.Accounts.Domain.Common;

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

        // CYC-2 (ADR-0012, decisao 7): corrigir nome, bandeira, final e limite.
        group.MapPut("/{id:guid}", async (
            Guid id, EditCreditCardRequest request, HttpContext http, EditCreditCardHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(
                new EditCreditCardCommand(id, owner.Value, request.Label, request.Brand, request.LastFourDigits, request.CreditLimit), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        // CYC-3 (ADR-0012, decisoes 3, 4 e 8): troca de ciclo a partir de uma fatura, e desfazer a pendente.
        group.MapPut("/{id:guid}/cycle", async (
            Guid id, ChangeCardCycleRequest request, HttpContext http, ChangeCreditCardCycleHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            if (!TryParseMonth(request.FromDueMonth, out var month))
            {
                return HttpResults.Problem(MonthInvalid);
            }

            var result = await handler.HandleAsync(
                new ChangeCreditCardCycleCommand(id, owner.Value, request.ClosingDay, request.DueDay, month), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapDelete("/{id:guid}/cycle/{fromDueMonth}", async (
            Guid id, string fromDueMonth, HttpContext http, RemoveCreditCardCycleHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            if (!TryParseMonth(fromDueMonth, out var month))
            {
                return HttpResults.Problem(MonthInvalid);
            }

            var result = await handler.HandleAsync(id, owner.Value, month, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
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

    private static readonly Error MonthInvalid = Error.Validation("card.month_invalid", "Mes invalido: use o formato 'yyyy-MM'.");

    private static bool TryParseMonth(string? value, out DateOnly month) =>
        DateOnly.TryParseExact((value ?? string.Empty) + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out month);
}

/// <summary>CYC-2: dados do cartao (a conta e o ciclo nao mudam aqui).</summary>
public sealed record EditCreditCardRequest(string? Label, string? Brand, string? LastFourDigits, decimal CreditLimit);

/// <summary>CYC-3: ciclo novo (dias de 1 a 28) e a fatura (mes de vencimento, <c>yyyy-MM</c>) em que comeca.</summary>
public sealed record ChangeCardCycleRequest(int ClosingDay, int DueDay, string? FromDueMonth);
