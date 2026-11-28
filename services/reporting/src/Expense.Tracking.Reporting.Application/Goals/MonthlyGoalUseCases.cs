using System.Globalization;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Forecast;
using Expense.Tracking.Reporting.Application.Invoices;
using Expense.Tracking.Reporting.Domain.Common;
using Expense.Tracking.Reporting.Domain.Forecast;
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

/// <param name="Kind">Forma da vigencia do mes; nula se o escopo nao tem meta nele.</param>
/// <param name="Target">Alvo do mes (valor, percentual das entradas ou teto); nulo sem meta.</param>
/// <param name="Outflows">Saidas fora do cartao + faturas que vencem no mes.</param>
/// <param name="Result">Entradas − saidas − faturas, igual ao <c>result</c> da previsao.</param>
/// <param name="CanStillSpend">Quanto ainda cabe gastar sem perder a meta; nulo sem meta.</param>
/// <param name="Situation"><c>Inside</c>, <c>Attention</c> ou <c>Outside</c>; nula sem meta.</param>
public sealed record MonthlyGoalScopeStatusDto(
    string Scope, string? Kind, decimal? Amount, int? Percent, decimal? Target,
    decimal Income, decimal Expense, decimal Invoices, decimal Outflows, decimal Result,
    decimal? CanStillSpend, string? Situation);

/// <param name="Month">Mes consultado, <c>yyyy-MM</c>.</param>
/// <param name="Notice">Lembrete para a tela: o cartao pesa no mes de vencimento da fatura.</param>
public sealed record MonthlyGoalStatusDto(
    string Month, string Notice, MonthlyGoalScopeStatusDto Personal, MonthlyGoalScopeStatusDto Business);

/// <summary>
/// MET-3 (ADR-0013, regras 2, 4 e 5): <c>GET /goals/monthly/status?month=</c>. O mes atual ou um futuro (ate o
/// limite da previsao), pelos numeros da propria previsao (o lancado e o previsto), para o resultado ser o mesmo.
/// Meses passados ficam no historico (MET-4).
/// </summary>
public sealed class GetMonthlyGoalStatusHandler(
    IMonthlyGoalRepository goals, GetCashForecastHandler forecast, InvoiceOptions options, TimeProvider timeProvider)
{
    public const string Notice =
        "Compras no cartão entram no mês de vencimento da fatura, como na previsão. Recorrências ainda não lançadas entram como previstas.";

    public static readonly Error MonthInPast = Error.Validation(
        "goal.month_in_past", "A situacao vale para o mes atual e os seguintes; meses passados estao no historico.");

    public static readonly Error MonthOutOfRange = Error.Validation(
        "goal.month_out_of_range", $"A situacao vai ate {CashForecastCalculator.MaxMonths} meses a partir do atual, como a previsao.");

    public async Task<Result<MonthlyGoalStatusDto>> HandleAsync(Guid ownerUserId, DateOnly? month, CancellationToken cancellationToken)
    {
        var today = CardInvoiceSource.Today(timeProvider, options);
        var current = new DateOnly(today.Year, today.Month, 1);
        var target = month is { } m ? new DateOnly(m.Year, m.Month, 1) : current;
        if (target < current)
        {
            return Result.Failure<MonthlyGoalStatusDto>(MonthInPast);
        }

        var months = (target.Year - current.Year) * 12 + target.Month - current.Month + 1;
        if (months > CashForecastCalculator.MaxMonths)
        {
            return Result.Failure<MonthlyGoalStatusDto>(MonthOutOfRange);
        }

        var cash = await forecast.HandleAsync(ownerUserId, months, cancellationToken);
        if (cash.IsFailure)
        {
            return Result.Failure<MonthlyGoalStatusDto>(cash.Error);
        }

        var flows = cash.Value.Months[^1];
        var all = await goals.ListAsync(ownerUserId, cancellationToken);
        return new MonthlyGoalStatusDto(
            target.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            Notice,
            StatusOf(GoalScope.Personal, flows.Personal, all, target),
            StatusOf(GoalScope.Business, flows.Business, all, target));
    }

    private static MonthlyGoalScopeStatusDto StatusOf(
        GoalScope scope, MonthFlowDto flow, IReadOnlyList<MonthlyGoal> all, DateOnly month)
    {
        var figures = new MonthFigures(flow.Income, flow.Expense, flow.Invoices);
        var term = all.Where(g => g.Scope == scope).Select(g => g.TermFor(month)).FirstOrDefault(t => t is not null);
        var evaluation = term is null ? null : MonthlyGoalStatusCalculator.Evaluate(term, figures);
        return new MonthlyGoalScopeStatusDto(
            GoalScopes.ToRoute(scope), term?.Kind.ToString(), term?.Amount, term?.Percent, evaluation?.Target,
            figures.Income, figures.Expense, figures.Invoices, figures.Outflows, figures.Result,
            evaluation?.CanStillSpend, evaluation?.Situation.ToString());
    }
}

/// <param name="Achieved">MET-4: se a meta do mes foi atingida; nulo se o escopo nao tinha meta nele.</param>
public sealed record MonthlyGoalHistoryScopeDto(
    string Scope, string? Kind, decimal? Amount, int? Percent, decimal? Target,
    decimal Income, decimal Expense, decimal Invoices, decimal Outflows, decimal Result, bool? Achieved);

/// <param name="Month">Mes fechado, <c>yyyy-MM</c>.</param>
public sealed record MonthlyGoalHistoryMonthDto(string Month, MonthlyGoalHistoryScopeDto Personal, MonthlyGoalHistoryScopeDto Business);

/// <summary>
/// MET-4 (ADR-0013, regras 2 e 5): <c>GET /goals/monthly/history?months=</c>, os meses fechados antes do atual (do mais
/// antigo ao mais recente), com a meta que valia, o resultado so com o que foi lancado (sem recorrencias previstas) e
/// se a meta foi atingida. As mesmas pecas da previsao: lancamentos fora do cartao pela data e o total das faturas
/// pelo mes de vencimento.
/// </summary>
public sealed class GetMonthlyGoalHistoryHandler(
    IMonthlyGoalRepository goals, ICardDirectory cards, IProjectionQueries projection, InvoiceOptions options, TimeProvider timeProvider)
{
    public const int DefaultMonths = 6;
    public const int MaxMonths = 24;

    public static readonly Error MonthsInvalid = Error.Validation(
        "goal.months_invalid", $"Informe de 1 a {MaxMonths} meses.");

    public async Task<Result<IReadOnlyList<MonthlyGoalHistoryMonthDto>>> HandleAsync(
        Guid ownerUserId, int months, CancellationToken cancellationToken)
    {
        if (months is < 1 or > MaxMonths)
        {
            return Result.Failure<IReadOnlyList<MonthlyGoalHistoryMonthDto>>(MonthsInvalid);
        }

        var today = CardInvoiceSource.Today(timeProvider, options);
        var current = new DateOnly(today.Year, today.Month, 1);
        var first = current.AddMonths(-months);
        var last = current.AddDays(-1);

        var movements = (await projection.GetCashMovementsAsync(ownerUserId, first, last, cancellationToken))
            .Select(m => new CashMovement(m.AccountScope, m.TransactionType == "Income", m.Amount, m.OccurredOn));
        var invoices = new List<ForecastInvoice>();
        foreach (var (card, scope) in await cards.ListWithScopesAsync(cancellationToken))
        {
            var loaded = await CardInvoiceSource.LoadAsync(projection, ownerUserId, card, today, cancellationToken);
            invoices.AddRange(loaded.Invoices
                .Where(i => i.Period.DueMonth >= first && i.Period.DueMonth < current)
                .Select(i => new ForecastInvoice(scope, i.Period.DueMonth, i.Total, i.Paid, i.Remaining)));
        }

        var flows = CashForecastCalculator.Calculate(first, months, movements, invoices);
        var all = await goals.ListAsync(ownerUserId, cancellationToken);
        return flows
            .Select(f => new MonthlyGoalHistoryMonthDto(
                f.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                HistoryOf(GoalScope.Personal, f.Personal, all, f.Month),
                HistoryOf(GoalScope.Business, f.Business, all, f.Month)))
            .ToList();
    }

    private static MonthlyGoalHistoryScopeDto HistoryOf(GoalScope scope, MonthFlow flow, IReadOnlyList<MonthlyGoal> all, DateOnly month)
    {
        var figures = new MonthFigures(flow.Income, flow.Expense, flow.Invoices);
        var term = all.Where(g => g.Scope == scope).Select(g => g.TermFor(month)).FirstOrDefault(t => t is not null);
        return new MonthlyGoalHistoryScopeDto(
            GoalScopes.ToRoute(scope), term?.Kind.ToString(), term?.Amount, term?.Percent,
            term is null ? null : MonthlyGoalStatusCalculator.TargetOf(term, figures),
            figures.Income, figures.Expense, figures.Invoices, figures.Outflows, figures.Result,
            term is null ? null : MonthlyGoalStatusCalculator.IsAchieved(term, figures));
    }
}
