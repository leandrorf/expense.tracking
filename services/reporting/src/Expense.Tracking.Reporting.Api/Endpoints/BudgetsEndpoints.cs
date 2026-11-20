using System.Globalization;
using System.Security.Claims;
using Expense.Tracking.Reporting.Api.Setup;
using Expense.Tracking.Reporting.Application.Budgets;
using Expense.Tracking.Reporting.Domain.Budgets;
using Expense.Tracking.Reporting.Domain.Common;

namespace Expense.Tracking.Reporting.Api.Endpoints;

/// <param name="StartMonth">Mes de vigencia no formato <c>yyyy-MM</c>.</param>
/// <param name="Currency">Codigo ISO 4217; BRL se omitido.</param>
public sealed record CreateBudgetRequest(string? Category, string? Scope, decimal Amount, string? StartMonth, string? Currency);

public static class BudgetsEndpoints
{
    public static IEndpointRouteBuilder MapBudgetsEndpoints(this IEndpointRouteBuilder app)
    {
        // BDG-2 (ADR-0002): orcamentos mensais por categoria e escopo, sempre restritos ao dono.
        var group = app.MapGroup("/budgets").WithTags("Budgets");

        group.MapGet("/", async (ClaimsPrincipal user, ListBudgetsHandler handler, CancellationToken ct) =>
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

            if (!DateOnly.TryParseExact(request.StartMonth, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startMonth))
            {
                return HttpResults.Problem(Error.Validation("budget.month_invalid", "Mes de vigencia invalido: use o formato 'yyyy-MM'."));
            }

            var result = await handler.HandleAsync(
                new CreateBudgetCommand(ownerUserId, request.Category, scope, request.Amount, request.Currency, startMonth), ct);

            return result.IsSuccess
                ? Results.Created($"/budgets/{result.Value.Id}", result.Value)
                : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        return app;
    }
}
