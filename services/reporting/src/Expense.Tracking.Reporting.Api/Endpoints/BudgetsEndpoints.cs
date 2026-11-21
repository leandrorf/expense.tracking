using System.Globalization;
using System.Security.Claims;
using Expense.Tracking.Reporting.Api.Setup;
using Expense.Tracking.Reporting.Application.Budgets;
using Expense.Tracking.Reporting.Domain.Budgets;
using Expense.Tracking.Reporting.Domain.Common;

namespace Expense.Tracking.Reporting.Api.Endpoints;

/// <param name="StartMonth">Mes de vigencia no formato <c>yyyy-MM</c>.</param>
/// <param name="Currency">Codigo ISO 4217; BRL se omitido.</param>
/// <param name="WarningThreshold">Percentual (1 a 100) a partir do qual o status e Atencao; padrao 80.</param>
public sealed record CreateBudgetRequest(
    string? Category, string? Scope, decimal Amount, string? StartMonth, string? Currency, int? WarningThreshold = null);

/// <param name="FromMonth">Mes (<c>yyyy-MM</c>) a partir do qual o novo valor vale.</param>
public sealed record ChangeBudgetAmountRequest(decimal Amount, string? FromMonth);

/// <param name="FromMonth">Mes (<c>yyyy-MM</c>) a partir do qual o orcamento deixa de valer.</param>
public sealed record CloseBudgetRequest(string? FromMonth);

public sealed record ChangeWarningThresholdRequest(int WarningThreshold);

public static class BudgetsEndpoints
{
    private static readonly Error InvalidMonth =
        Error.Validation("budget.month_invalid", "Mes invalido: use o formato 'yyyy-MM'.");

    public static IEndpointRouteBuilder MapBudgetsEndpoints(this IEndpointRouteBuilder app)
    {
        // BDG-2/BDG-3/BDG-4 (ADR-0002): orcamentos mensais por categoria e escopo, sempre restritos ao dono.
        var group = app.MapGroup("/budgets").WithTags("Budgets");

        // Sem ?month= lista todos (com o valor da vigencia mais recente); com mes, so os que valem
        // nele, com o valor daquele mes.
        group.MapGet("/", async (string? month, ClaimsPrincipal user, ListBudgetsHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            DateOnly? filter = null;
            if (month is not null)
            {
                if (!TryParseMonth(month, out var parsed))
                {
                    return HttpResults.Problem(InvalidMonth);
                }

                filter = parsed;
            }

            return Results.Ok(await handler.HandleAsync(ownerUserId, filter, ct));
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        // BDG-4: limite, consumido, restante e situacao de cada orcamento que vale no mes.
        group.MapGet("/status", async (string? month, ClaimsPrincipal user, GetBudgetStatusHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            if (!TryParseMonth(month, out var parsed))
            {
                return HttpResults.Problem(InvalidMonth);
            }

            return Results.Ok(await handler.HandleAsync(ownerUserId, parsed, ct));
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        // BDG-7: categorias ja usadas (lancamentos + orcamentos), sem repetir variacoes de digitacao.
        group.MapGet("/categories", async (ClaimsPrincipal user, GetCategorySuggestionsHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await handler.HandleAsync(ownerUserId, ct));
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        group.MapPost("/", async (CreateBudgetRequest request, ClaimsPrincipal user, CreateBudgetHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            if (!Enum.TryParse<BudgetScope>(request.Scope, ignoreCase: true, out var scope)
                || !Enum.IsDefined(scope)
                || int.TryParse(request.Scope, out _))
            {
                return HttpResults.Problem(Error.Validation("budget.scope_invalid", "Escopo invalido: use 'Personal' ou 'Business'."));
            }

            if (!TryParseMonth(request.StartMonth, out var startMonth))
            {
                return HttpResults.Problem(InvalidMonth);
            }

            var result = await handler.HandleAsync(
                new CreateBudgetCommand(
                    ownerUserId, request.Category, scope, request.Amount, request.Currency, startMonth, request.WarningThreshold), ct);

            return result.IsSuccess
                ? Results.Created($"/budgets/{result.Value.Id}", result.Value)
                : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapPut("/{id:guid}/amount", async (
            Guid id, ChangeBudgetAmountRequest request, ClaimsPrincipal user, ChangeBudgetHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            if (!TryParseMonth(request.FromMonth, out var fromMonth))
            {
                return HttpResults.Problem(InvalidMonth);
            }

            var result = await handler.HandleAsync(new ChangeBudgetAmountCommand(id, ownerUserId, request.Amount, fromMonth), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapPost("/{id:guid}/close", async (
            Guid id, CloseBudgetRequest request, ClaimsPrincipal user, ChangeBudgetHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            if (!TryParseMonth(request.FromMonth, out var fromMonth))
            {
                return HttpResults.Problem(InvalidMonth);
            }

            var result = await handler.HandleAsync(new CloseBudgetCommand(id, ownerUserId, fromMonth), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        // BDG-10: limiar de atencao do orcamento (vale para todos os meses dele).
        group.MapPut("/{id:guid}/warning-threshold", async (
            Guid id, ChangeWarningThresholdRequest request, ClaimsPrincipal user, ChangeBudgetHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(new ChangeWarningThresholdCommand(id, ownerUserId, request.WarningThreshold), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        return app;
    }

    private static bool TryParseMonth(string? value, out DateOnly month) =>
        DateOnly.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out month);
}
