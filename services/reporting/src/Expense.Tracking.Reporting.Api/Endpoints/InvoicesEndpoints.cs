using System.Globalization;
using System.Security.Claims;
using Expense.Tracking.Reporting.Api.Setup;
using Expense.Tracking.Reporting.Application.Invoices;
using Expense.Tracking.Reporting.Domain.Common;

namespace Expense.Tracking.Reporting.Api.Endpoints;

/// <summary>RCN-2: total da fatura do banco (obrigatorio).</summary>
public sealed record ReconcileInvoiceRequest(decimal? BankTotal);

public static class InvoicesEndpoints
{
    private static readonly Error InvalidMonth =
        Error.Validation("invoice.month_invalid", "Mes invalido: use o formato 'yyyy-MM'.");

    public static IEndpointRouteBuilder MapInvoicesEndpoints(this IEndpointRouteBuilder app)
    {
        // INV-4 (ADR-0005): faturas de um cartao do dono. from/to recortam pelo mes de vencimento
        // (yyyy-MM); sem from, comeca 12 faturas antes da atual; sem to, vai ate a ultima parcela.
        app.MapGet("/reports/cards/{id:guid}/invoices", async (
            Guid id, string? from, string? to, ClaimsPrincipal user, GetCardInvoicesHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            if (!TryParseOptionalMonth(from, out var fromMonth) || !TryParseOptionalMonth(to, out var toMonth))
            {
                return HttpResults.Problem(InvalidMonth);
            }

            var result = await handler.HandleAsync(ownerUserId, id, fromMonth, toMonth, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        })
        .WithTags("Reports")
        .RequireAuthorization(AuthenticationSetup.ReadPolicy);

        // RCN-2 (ADR-0011, decisoes 7 e 8): conferir a fatura com a do banco pelo total.
        var reconciliation = app.MapGroup("/reports/cards/{id:guid}/invoices/{dueMonth}/reconciliation").WithTags("Reports");
        reconciliation.MapGet("/", async (
            Guid id, string dueMonth, ClaimsPrincipal user, InvoiceReconciliationHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            if (!TryParseOptionalMonth(dueMonth, out var month) || month is null)
            {
                return HttpResults.Problem(InvalidMonth);
            }

            var result = await handler.GetAsync(ownerUserId, id, month.Value, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        reconciliation.MapPut("/", async (
            Guid id, string dueMonth, ReconcileInvoiceRequest request, ClaimsPrincipal user, InvoiceReconciliationHandler handler,
            CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            if (!TryParseOptionalMonth(dueMonth, out var month) || month is null)
            {
                return HttpResults.Problem(InvalidMonth);
            }

            if (request.BankTotal is not { } bankTotal)
            {
                return HttpResults.Problem(Domain.Invoices.InvoiceReconciliation.BankTotalInvalid);
            }

            var result = await handler.ReconcileAsync(ownerUserId, id, month.Value, bankTotal, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        reconciliation.MapDelete("/", async (
            Guid id, string dueMonth, ClaimsPrincipal user, InvoiceReconciliationHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            if (!TryParseOptionalMonth(dueMonth, out var month) || month is null)
            {
                return HttpResults.Problem(InvalidMonth);
            }

            var result = await handler.UndoAsync(ownerUserId, id, month.Value, ct);
            return result.IsSuccess ? Results.NoContent() : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        // INV-10 (ADR-0005, decisao 9): limite disponivel de cada cartao do usuario.
        app.MapGet("/reports/cards/limits", async (ClaimsPrincipal user, GetCardLimitsHandler handler, CancellationToken ct) =>
            Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId)
                ? Results.Ok(await handler.HandleAsync(ownerUserId, ct))
                : Results.Unauthorized())
        .WithTags("Reports")
        .RequireAuthorization(AuthenticationSetup.ReadPolicy);

        return app;
    }

    private static bool TryParseOptionalMonth(string? value, out DateOnly? month)
    {
        month = null;
        if (value is null)
        {
            return true;
        }

        if (!DateOnly.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        month = parsed;
        return true;
    }
}
