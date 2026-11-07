using Expense.Tracking.Transactions.Api.Setup;
using Expense.Tracking.Transactions.Application.Transactions;
using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Api.Endpoints;

public sealed record CreateTransactionRequest(
    Guid AccountId,
    string? Type,
    string PaymentMethod,
    Guid? CreditCardId,
    string Description,
    string Category,
    decimal Amount,
    string? Currency,
    DateOnly OccurredOn,
    string? Notes);

public static class TransactionsEndpoints
{
    public static IEndpointRouteBuilder MapTransactionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/transactions").WithTags("Transactions");

        group.MapGet("/", async (
            HttpContext http,
            ListTransactionsHandler handler,
            CancellationToken ct,
            Guid? accountId,
            string? category,
            DateOnly? from,
            DateOnly? to) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var items = await handler.HandleAsync(
                new ListTransactionsCommand(owner.Value, accountId, category, from, to), ct);
            return Results.Ok(items);
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, GetTransactionHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(id, owner.Value, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        group.MapPost("/", async (CreateTransactionRequest request, HttpContext http, CreateTransactionHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var type = TransactionType.Expense;
            if (!string.IsNullOrWhiteSpace(request.Type) && !Enum.TryParse(request.Type, ignoreCase: true, out type))
            {
                return HttpResults.Problem(Error.Validation("transaction.type_invalid", "Tipo invalido: use 'Expense' ou 'Income'."));
            }

            if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod, ignoreCase: true, out var paymentMethod))
            {
                return HttpResults.Problem(Error.Validation("transaction.payment_method_invalid", "Meio de pagamento invalido."));
            }

            var result = await handler.HandleAsync(
                new CreateTransactionCommand(
                    owner.Value, request.AccountId, type, paymentMethod, request.CreditCardId,
                    request.Description, request.Category, request.Amount, request.Currency,
                    request.OccurredOn, request.Notes),
                ct);

            return result.IsSuccess
                ? Results.Created($"/transactions/{result.Value.Id}", result.Value)
                : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapDelete("/{id:guid}", async (Guid id, HttpContext http, DeleteTransactionHandler handler, CancellationToken ct) =>
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
