using Expense.Tracking.Reporting.Domain.Common;
using Expense.Tracking.Reporting.Domain.Goals;

namespace Expense.Tracking.Reporting.Application.Goals;

/// <param name="FromMonth">Mes (<c>yyyy-MM</c>) a partir do qual a vigencia vale.</param>
/// <param name="Kind"><c>SaveAmount</c>, <c>SavePercent</c> ou <c>SpendCeiling</c>.</param>
public sealed record MonthlyGoalTermDto(string FromMonth, string Kind, decimal? Amount, int? Percent);

/// <param name="Scope"><c>personal</c> ou <c>business</c>, como na rota.</param>
/// <param name="ClosedFromMonth">Mes a partir do qual a meta nao vale mais; nulo se aberta.</param>
public sealed record MonthlyGoalDto(
    Guid Id, string Scope, string StartMonth, string? ClosedFromMonth, IReadOnlyList<MonthlyGoalTermDto> Terms)
{
    public static MonthlyGoalDto From(MonthlyGoal goal) => new(
        goal.Id,
        GoalScopes.ToRoute(goal.Scope),
        ToMonth(goal.StartMonth),
        goal.ClosedFromMonth is { } closed ? ToMonth(closed) : null,
        goal.Terms.Select(t => new MonthlyGoalTermDto(ToMonth(t.EffectiveFrom), t.Kind.ToString(), t.Amount, t.Percent)).ToList());

    private static string ToMonth(DateOnly month) => month.ToString("yyyy-MM");
}

/// <summary>MET: escopo na rota e nas respostas (<c>personal</c>/<c>business</c>).</summary>
public static class GoalScopes
{
    public static string ToRoute(GoalScope scope) => scope == GoalScope.Business ? "business" : "personal";

    public static bool TryParse(string? value, out GoalScope scope)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "personal":
                scope = GoalScope.Personal;
                return true;
            case "business":
                scope = GoalScope.Business;
                return true;
            default:
                scope = default;
                return false;
        }
    }
}

/// <summary>MET-2: metas mensais do usuario no banco do reporting (ADR-0013).</summary>
public interface IMonthlyGoalRepository
{
    Task<IReadOnlyList<MonthlyGoal>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>A meta aberta do dono no escopo, para alterar; nula se nao ha.</summary>
    Task<MonthlyGoal?> FindOpenAsync(Guid ownerUserId, GoalScope scope, CancellationToken cancellationToken);

    /// <summary>O encerramento mais recente das metas do dono no escopo; nulo se nenhuma foi encerrada.</summary>
    Task<DateOnly?> LatestClosedFromAsync(Guid ownerUserId, GoalScope scope, CancellationToken cancellationToken);

    /// <summary>Grava a meta; devolve <c>false</c> se o indice unico acusar outra aberta (corrida).</summary>
    Task<bool> TryAddAsync(MonthlyGoal goal, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record SetMonthlyGoalCommand(
    Guid OwnerUserId, GoalScope Scope, MonthlyGoalKind Kind, decimal? Amount, int? Percent, DateOnly FromMonth);

/// <param name="Created">Se a meta foi criada agora (sem meta aberta no escopo) ou mudou a partir do mes.</param>
public sealed record SetMonthlyGoalResult(MonthlyGoalDto Goal, bool Created);

/// <summary>
/// MET-2 (ADR-0013, regra 3): <c>PUT /goals/monthly/{scope}</c>. Sem meta aberta no escopo, cria uma comecando
/// no mes (depois do encerramento da anterior, se houver); com meta aberta, muda a forma e o valor a partir do mes.
/// </summary>
public sealed class SetMonthlyGoalHandler(IMonthlyGoalRepository goals, TimeProvider clock)
{
    private static readonly Error Duplicate =
        Error.Conflict("goal.duplicate", "Outra requisicao criou a meta deste escopo ao mesmo tempo; tente de novo.");

    public async Task<Result<SetMonthlyGoalResult>> HandleAsync(SetMonthlyGoalCommand command, CancellationToken cancellationToken)
    {
        var open = await goals.FindOpenAsync(command.OwnerUserId, command.Scope, cancellationToken);
        if (open is not null)
        {
            var changed = open.Change(command.Kind, command.Amount, command.Percent, command.FromMonth);
            if (changed.IsFailure)
            {
                return Result.Failure<SetMonthlyGoalResult>(changed.Error);
            }

            await goals.SaveChangesAsync(cancellationToken);
            return new SetMonthlyGoalResult(MonthlyGoalDto.From(open), Created: false);
        }

        var created = MonthlyGoal.Create(
            command.OwnerUserId, command.Scope, command.Kind, command.Amount, command.Percent, command.FromMonth, clock.GetUtcNow());
        if (created.IsFailure)
        {
            return Result.Failure<SetMonthlyGoalResult>(created.Error);
        }

        var goal = created.Value;
        if (await goals.LatestClosedFromAsync(command.OwnerUserId, command.Scope, cancellationToken) is { } closed
            && goal.StartMonth < closed)
        {
            return Result.Failure<SetMonthlyGoalResult>(GoalErrors.StartBeforePreviousClose);
        }

        if (!await goals.TryAddAsync(goal, cancellationToken))
        {
            return Result.Failure<SetMonthlyGoalResult>(Duplicate);
        }

        return new SetMonthlyGoalResult(MonthlyGoalDto.From(goal), Created: true);
    }
}

/// <summary>MET-2: <c>DELETE /goals/monthly/{scope}?fromMonth=</c> encerra a meta aberta do escopo.</summary>
public sealed class CloseMonthlyGoalHandler(IMonthlyGoalRepository goals)
{
    public async Task<Result<MonthlyGoalDto>> HandleAsync(
        Guid ownerUserId, GoalScope scope, DateOnly fromMonth, CancellationToken cancellationToken)
    {
        var open = await goals.FindOpenAsync(ownerUserId, scope, cancellationToken);
        if (open is null)
        {
            return Result.Failure<MonthlyGoalDto>(GoalErrors.NotFound);
        }

        var closed = open.Close(fromMonth);
        if (closed.IsFailure)
        {
            return Result.Failure<MonthlyGoalDto>(closed.Error);
        }

        await goals.SaveChangesAsync(cancellationToken);
        return MonthlyGoalDto.From(open);
    }
}

/// <summary>MET-2: <c>GET /goals/monthly</c>, as metas do usuario (abertas e encerradas) com as vigencias.</summary>
public sealed class ListMonthlyGoalsHandler(IMonthlyGoalRepository goals)
{
    public async Task<IReadOnlyList<MonthlyGoalDto>> HandleAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        (await goals.ListAsync(ownerUserId, cancellationToken))
            .OrderBy(g => g.Scope)
            .ThenBy(g => g.StartMonth)
            .Select(MonthlyGoalDto.From)
            .ToList();
}
