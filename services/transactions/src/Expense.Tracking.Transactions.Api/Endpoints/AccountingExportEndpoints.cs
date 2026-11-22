using System.Globalization;
using Expense.Tracking.Transactions.Api.Setup;
using Expense.Tracking.Transactions.Application.AccountingExport;
using Expense.Tracking.Transactions.Domain.Common;

namespace Expense.Tracking.Transactions.Api.Endpoints;

public static class AccountingExportEndpoints
{
    private static readonly Error InvalidMonth =
        Error.Validation("accounting_export.month_invalid", "Mes invalido: use o formato 'yyyy-MM'.");

    public static IEndpointRouteBuilder MapAccountingExportEndpoints(this IEndpointRouteBuilder app)
    {
        // CTB-2/CTB-3 (ADR-0006, decisao 9): o movimento PJ do mes num ZIP, para o contador.
        app.MapGet("/transactions/accounting-export", async (
            HttpContext http, ExportAccountingHandler handler, ILoggerFactory loggers, CancellationToken ct,
            string? month, Guid? accountId) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            if (!DateOnly.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first))
            {
                return HttpResults.Problem(InvalidMonth);
            }

            var result = await handler.HandleAsync(new ExportAccountingQuery(owner.Value, first, accountId), ct);
            if (result.IsFailure)
            {
                return HttpResults.Problem(result.Error);
            }

            // So o mes e as quantidades: o conteudo do arquivo nunca vai para log ou trace.
            loggers.CreateLogger("AccountingExport").LogInformation(
                "Exportacao contabil de {Month}: {Accounts} conta(s), {Lines} lancamento(s), {Payments} pagamento(s) de fatura.",
                month, result.Value.Accounts, result.Value.Lines, result.Value.Payments);
            return Results.File(result.Value.Content, "application/zip", result.Value.FileName);
        })
        .WithTags("AccountingExport")
        .RequireAuthorization(AuthenticationSetup.ReadPolicy);

        return app;
    }
}
