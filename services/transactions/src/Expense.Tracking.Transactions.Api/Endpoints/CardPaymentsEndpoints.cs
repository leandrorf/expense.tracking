using System.Globalization;
using Expense.Tracking.Transactions.Api.Setup;
using Expense.Tracking.Transactions.Application.CardPayments;
using Expense.Tracking.Transactions.Domain.Common;

namespace Expense.Tracking.Transactions.Api.Endpoints;

/// <summary>INV-6: corpo do registro de pagamento. O mes da fatura vai como "yyyy-MM".</summary>
public sealed record RegisterCardPaymentRequest(
    Guid CreditCardId,
    string? InvoiceDueMonth,
    Guid AccountId,
    decimal Amount,
    string? Currency,
    DateOnly PaidOn);

public static class CardPaymentsEndpoints
{
    private static readonly Error InvalidMonth =
        Error.Validation("card_payment.month_invalid", "Mes da fatura invalido: use o formato 'yyyy-MM'.");

    public static IEndpointRouteBuilder MapCardPaymentsEndpoints(this IEndpointRouteBuilder app)
    {
        // INV-6 (ADR-0005, decisoes 6 e 11): pagamentos de fatura, fora dos lancamentos.
        var group = app.MapGroup("/transactions/card-payments").WithTags("CardPayments");

        group.MapGet("/", async (
            HttpContext http, ListCardPaymentsHandler handler, CancellationToken ct, Guid? creditCardId, string? invoiceDueMonth) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            DateOnly? month = null;
            if (invoiceDueMonth is not null)
            {
                if (!TryParseMonth(invoiceDueMonth, out var parsed))
                {
                    return HttpResults.Problem(InvalidMonth);
                }

                month = parsed;
            }

            return Results.Ok(await handler.HandleAsync(owner.Value, creditCardId, month, ct));
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        group.MapPost("/", async (
            RegisterCardPaymentRequest request, HttpContext http, RegisterCardPaymentHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            if (!TryParseMonth(request.InvoiceDueMonth, out var month))
            {
                return HttpResults.Problem(InvalidMonth);
            }

            var result = await handler.HandleAsync(
                new RegisterCardPaymentCommand(
                    owner.Value, request.CreditCardId, month, request.AccountId, request.Amount, request.Currency, request.PaidOn),
                ct);

            return result.IsSuccess
                ? Results.Created($"/transactions/card-payments/{result.Value.Id}", result.Value)
                : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapDelete("/{id:guid}", async (Guid id, HttpContext http, DeleteCardPaymentHandler handler, CancellationToken ct) =>
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

    private static bool TryParseMonth(string? value, out DateOnly month) =>
        DateOnly.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out month);
}
