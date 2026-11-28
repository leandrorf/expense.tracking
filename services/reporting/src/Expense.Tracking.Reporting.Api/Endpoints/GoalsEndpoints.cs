using System.Globalization;
using System.Security.Claims;
using Expense.Tracking.Reporting.Api.Setup;
using Expense.Tracking.Reporting.Application.Goals;
using Expense.Tracking.Reporting.Domain.Goals;

namespace Expense.Tracking.Reporting.Api.Endpoints;

/// <param name="Kind"><c>SaveAmount</c>, <c>SavePercent</c> ou <c>SpendCeiling</c>.</param>
/// <param name="Amount">Valor a guardar ou teto (formas <c>SaveAmount</c> e <c>SpendCeiling</c>).</param>
/// <param name="Percent">Percentual das entradas, de 1 a 100 (forma <c>SavePercent</c>).</param>
/// <param name="FromMonth">Mes (<c>yyyy-MM</c>) a partir do qual vale.</param>
public sealed record SetMonthlyGoalRequest(string? Kind, decimal? Amount, int? Percent, string? FromMonth);

/// <param name="Scope"><c>personal</c> ou <c>business</c>.</param>
/// <param name="Deadline">Mes do prazo (<c>yyyy-MM</c>), opcional.</param>
public sealed record CreateSavingsObjectiveRequest(string? Scope, string? Name, decimal Target, string? Deadline);

/// <param name="Deadline">Mes do prazo (<c>yyyy-MM</c>); nulo tira o prazo.</param>
public sealed record EditSavingsObjectiveRequest(string? Name, decimal Target, string? Deadline);

/// <param name="Date">Data do aporte (<c>yyyy-MM-dd</c>), ate hoje.</param>
/// <param name="Amount">Valor guardado; negativo = retirada.</param>
public sealed record AddSavingsContributionRequest(string? Date, decimal Amount, string? Note);

public static class GoalsEndpoints
{
    public static IEndpointRouteBuilder MapGoalsEndpoints(this IEndpointRouteBuilder app)
    {
        // MET (ADR-0013): metas de gastos e de economia, sempre restritas ao dono.
        var group = app.MapGroup("/goals").WithTags("Goals");

        // MET-2: as metas mensais (abertas e encerradas) com as vigencias.
        group.MapGet("/monthly", async (ClaimsPrincipal user, ListMonthlyGoalsHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await handler.HandleAsync(ownerUserId, ct));
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        // MET-3: situacao e "ainda pode gastar" de PF e PJ no mes atual (padrao) ou num futuro.
        group.MapGet("/monthly/status", async (string? month, ClaimsPrincipal user, GetMonthlyGoalStatusHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            DateOnly? parsed = null;
            if (month is not null)
            {
                if (!TryParseMonth(month, out var value))
                {
                    return HttpResults.Problem(GoalErrors.MonthInvalid);
                }

                parsed = value;
            }

            var result = await handler.HandleAsync(ownerUserId, parsed, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        // MET-2: cria a meta do escopo (201) ou muda a forma e o valor a partir do mes (200).
        group.MapPut("/monthly/{scope}", async (
            string scope, SetMonthlyGoalRequest request, ClaimsPrincipal user, SetMonthlyGoalHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            if (!GoalScopes.TryParse(scope, out var goalScope))
            {
                return HttpResults.Problem(GoalErrors.ScopeInvalid);
            }

            if (!TryParseKind(request.Kind, out var kind))
            {
                return HttpResults.Problem(GoalErrors.KindInvalid);
            }

            if (!TryParseMonth(request.FromMonth, out var fromMonth))
            {
                return HttpResults.Problem(GoalErrors.MonthInvalid);
            }

            var result = await handler.HandleAsync(
                new SetMonthlyGoalCommand(ownerUserId, goalScope, kind, request.Amount, request.Percent, fromMonth), ct);
            if (result.IsFailure)
            {
                return HttpResults.Problem(result.Error);
            }

            return result.Value.Created
                ? Results.Created($"/goals/monthly/{result.Value.Goal.Scope}", result.Value.Goal)
                : Results.Ok(result.Value.Goal);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        // MET-2: encerra a meta aberta do escopo a partir do mes.
        group.MapDelete("/monthly/{scope}", async (
            string scope, string? fromMonth, ClaimsPrincipal user, CloseMonthlyGoalHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            if (!GoalScopes.TryParse(scope, out var goalScope))
            {
                return HttpResults.Problem(GoalErrors.ScopeInvalid);
            }

            if (!TryParseMonth(fromMonth, out var month))
            {
                return HttpResults.Problem(GoalErrors.MonthInvalid);
            }

            var result = await handler.HandleAsync(ownerUserId, goalScope, month, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        // MET-5 (ADR-0013, regra 6): objetivos e aportes.
        group.MapGet("/objectives", async (bool? includeArchived, ClaimsPrincipal user, SavingsObjectiveHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await handler.ListAsync(ownerUserId, includeArchived ?? false, ct));
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        group.MapPost("/objectives", async (
            CreateSavingsObjectiveRequest request, ClaimsPrincipal user, SavingsObjectiveHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            if (!GoalScopes.TryParse(request.Scope, out var scope))
            {
                return HttpResults.Problem(GoalErrors.ScopeInvalid);
            }

            if (!TryParseDeadline(request.Deadline, out var deadline))
            {
                return HttpResults.Problem(GoalErrors.DeadlineInvalid);
            }

            var result = await handler.CreateAsync(new CreateSavingsObjectiveCommand(ownerUserId, scope, request.Name, request.Target, deadline), ct);
            return result.IsSuccess
                ? Results.Created($"/goals/objectives/{result.Value.Id}", result.Value)
                : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapPut("/objectives/{id:guid}", async (
            Guid id, EditSavingsObjectiveRequest request, ClaimsPrincipal user, SavingsObjectiveHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            if (!TryParseDeadline(request.Deadline, out var deadline))
            {
                return HttpResults.Problem(GoalErrors.DeadlineInvalid);
            }

            var result = await handler.EditAsync(new EditSavingsObjectiveCommand(id, ownerUserId, request.Name, request.Target, deadline), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapPost("/objectives/{id:guid}/archive", async (Guid id, ClaimsPrincipal user, SavingsObjectiveHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            var result = await handler.ArchiveAsync(id, ownerUserId, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapPost("/objectives/{id:guid}/unarchive", async (Guid id, ClaimsPrincipal user, SavingsObjectiveHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            var result = await handler.UnarchiveAsync(id, ownerUserId, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapPost("/objectives/{id:guid}/contributions", async (
            Guid id, AddSavingsContributionRequest request, ClaimsPrincipal user, SavingsObjectiveHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return HttpResults.Problem(GoalErrors.ContributionInvalid);
            }

            var result = await handler.AddContributionAsync(new AddSavingsContributionCommand(id, ownerUserId, date, request.Amount, request.Note), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapDelete("/objectives/{id:guid}/contributions/{contributionId:guid}", async (
            Guid id, Guid contributionId, ClaimsPrincipal user, SavingsObjectiveHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            var result = await handler.RemoveContributionAsync(id, ownerUserId, contributionId, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        return app;
    }

    /// <summary>Prazo opcional: vazio ou nulo = sem prazo; senao <c>yyyy-MM</c>.</summary>
    private static bool TryParseDeadline(string? value, out DateOnly? deadline)
    {
        deadline = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!TryParseMonth(value, out var month))
        {
            return false;
        }

        deadline = month;
        return true;
    }

    private static bool TryParseKind(string? value, out MonthlyGoalKind kind) =>
        Enum.TryParse(value, ignoreCase: true, out kind) && Enum.IsDefined(kind) && !int.TryParse(value, out _);

    private static bool TryParseMonth(string? value, out DateOnly month) =>
        DateOnly.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out month);
}
