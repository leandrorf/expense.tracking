using System.Globalization;
using Expense.Tracking.Transactions.Api.Setup;
using Expense.Tracking.Transactions.Application.AccountingExport;
using Expense.Tracking.Transactions.Domain.Common;

namespace Expense.Tracking.Transactions.Api.Endpoints;

/// <summary>CTB-5: corpo da associacao de uma categoria a uma conta contabil (o nome e opcional).</summary>
public sealed record SetLedgerAccountRequest(string? Category, string? Code, string? Name);

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

        // CTB-5 (ADR-0006, decisao 7): plano de contas, categoria -> conta contabil.
        var ledger = app.MapGroup("/transactions/ledger-accounts").WithTags("AccountingExport");

        ledger.MapGet("/", async (HttpContext http, ListLedgerAccountsHandler handler, CancellationToken ct) =>
            http.User.GetUserId() is { } owner ? Results.Ok(await handler.HandleAsync(owner, ct)) : Results.Unauthorized())
        .RequireAuthorization(AuthenticationSetup.ReadPolicy);

        ledger.MapPut("/", async (SetLedgerAccountRequest request, HttpContext http, SetLedgerAccountHandler handler, CancellationToken ct) =>
        {
            if (http.User.GetUserId() is not { } owner)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(new SetLedgerAccountCommand(owner, request.Category, request.Code, request.Name), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        })
        .RequireAuthorization(AuthenticationSetup.WritePolicy);

        // A categoria vai na query: e texto livre (pode ter "/" ou "%").
        ledger.MapDelete("/", async (string? category, HttpContext http, DeleteLedgerAccountHandler handler, CancellationToken ct) =>
        {
            if (http.User.GetUserId() is not { } owner)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(owner, category, ct);
            return result.IsSuccess ? Results.NoContent() : HttpResults.Problem(result.Error);
        })
        .RequireAuthorization(AuthenticationSetup.WritePolicy);

        // CTB-4: quantidades e totais do mesmo pacote, sem gerar o arquivo, para a tela.
        app.MapGet("/transactions/accounting-export/preview", async (
            HttpContext http, ExportAccountingHandler handler, CancellationToken ct, string? month, Guid? accountId) =>
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

            var result = await handler.PreviewAsync(new ExportAccountingQuery(owner.Value, first, accountId), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        })
        .WithTags("AccountingExport")
        .RequireAuthorization(AuthenticationSetup.ReadPolicy);

        return app;
    }
}
