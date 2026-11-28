using Expense.Tracking.Reporting.Domain.Common;

namespace Expense.Tracking.Reporting.Domain.Goals;

/// <summary>MET (ADR-0013): escopo da meta, o mesmo PF/PJ das contas.</summary>
public enum GoalScope
{
    Personal = 1,
    Business = 2
}

/// <summary>MET-2 (ADR-0013, regra 3): forma da meta mensal.</summary>
public enum MonthlyGoalKind
{
    /// <summary>Guardar um valor: resultado do mes ≥ valor.</summary>
    SaveAmount = 1,

    /// <summary>Guardar um percentual das entradas: resultado do mes ≥ percentual × entradas.</summary>
    SavePercent = 2,

    /// <summary>Teto de gastos: saidas fora do cartao + faturas do mes ≤ valor.</summary>
    SpendCeiling = 3
}

/// <summary>MET-2: forma e valor da meta a partir de um mes, ate a proxima vigencia.</summary>
public sealed class MonthlyGoalTerm
{
    private MonthlyGoalTerm()
    {
    }

    internal MonthlyGoalTerm(DateOnly effectiveFrom, MonthlyGoalKind kind, decimal? amount, int? percent)
    {
        EffectiveFrom = effectiveFrom;
        Set(kind, amount, percent);
    }

    /// <summary>Primeiro dia do mes em que esta vigencia passa a valer.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    public MonthlyGoalKind Kind { get; private set; }

    /// <summary>Valor a guardar ou teto; nulo na forma <see cref="MonthlyGoalKind.SavePercent"/>.</summary>
    public decimal? Amount { get; private set; }

    /// <summary>Percentual das entradas (1 a 100); so na forma <see cref="MonthlyGoalKind.SavePercent"/>.</summary>
    public int? Percent { get; private set; }

    internal void Set(MonthlyGoalKind kind, decimal? amount, int? percent)
    {
        Kind = kind;
        Amount = kind == MonthlyGoalKind.SavePercent ? null : amount;
        Percent = kind == MonthlyGoalKind.SavePercent ? percent : null;
    }
}

/// <summary>
/// MET-2 (ADR-0013, regras 1 e 3): meta do mes inteiro num escopo PF/PJ, sem categoria. Muda por vigencias
/// (a forma pode mudar de uma para outra) sem alterar os meses anteriores e pode ser encerrada a partir de um
/// mes, como o limite dos orcamentos. Dado escrito pelo usuario: fica no <c>expense_reporting</c>, fora da
/// projecao.
/// </summary>
public sealed class MonthlyGoal
{
    public const decimal MaxAmount = 1_000_000_000m;

    private readonly List<MonthlyGoalTerm> _terms = [];

    private MonthlyGoal()
    {
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public GoalScope Scope { get; private set; }

    /// <summary>
    /// Igual a <see cref="Scope"/> enquanto a meta esta aberta e nulo depois de encerrada: o indice unico do
    /// banco garante uma so meta aberta por dono e escopo (o MySQL aceita varios nulos num indice unico).
    /// </summary>
    public GoalScope? OpenScope { get; private set; }

    /// <summary>Primeiro dia do mes em que a meta passa a valer.</summary>
    public DateOnly StartMonth { get; private set; }

    /// <summary>Primeiro dia do mes em que a meta deixa de valer; nulo enquanto aberta.</summary>
    public DateOnly? ClosedFromMonth { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Vigencias em ordem de mes; a primeira comeca em <see cref="StartMonth"/>.</summary>
    public IReadOnlyList<MonthlyGoalTerm> Terms => _terms.OrderBy(t => t.EffectiveFrom).ToList();

    public static Result<MonthlyGoal> Create(
        Guid ownerUserId, GoalScope scope, MonthlyGoalKind kind, decimal? amount, int? percent, DateOnly startMonth,
        DateTimeOffset now)
    {
        if (!Enum.IsDefined(scope))
        {
            return Result.Failure<MonthlyGoal>(GoalErrors.ScopeInvalid);
        }

        if (Validate(kind, amount, percent) is { } error)
        {
            return Result.Failure<MonthlyGoal>(error);
        }

        var start = FirstDayOf(startMonth);
        var goal = new MonthlyGoal
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Scope = scope,
            OpenScope = scope,
            StartMonth = start,
            CreatedAt = now
        };
        goal._terms.Add(new MonthlyGoalTerm(start, kind, amount, percent));
        return goal;
    }

    /// <summary>A vigencia que vale no mes, ou nula se a meta nao vale nele (antes do inicio ou encerrada).</summary>
    public MonthlyGoalTerm? TermFor(DateOnly month)
    {
        var first = FirstDayOf(month);
        if (first < StartMonth || (ClosedFromMonth is { } closed && first >= closed))
        {
            return null;
        }

        return Terms.Last(t => t.EffectiveFrom <= first);
    }

    /// <summary>
    /// MET-2: muda a forma e o valor a partir de um mes. Os meses anteriores mantem a vigencia que tinham; as
    /// vigencias que comecavam depois de <paramref name="fromMonth"/> sao substituidas por esta.
    /// </summary>
    public Result Change(MonthlyGoalKind kind, decimal? amount, int? percent, DateOnly fromMonth)
    {
        if (Validate(kind, amount, percent) is { } error)
        {
            return Result.Failure(error);
        }

        var from = FirstDayOf(fromMonth);
        if (from < StartMonth)
        {
            return Result.Failure(GoalErrors.MonthBeforeStart);
        }

        if (ClosedFromMonth is { } closed && from >= closed)
        {
            return Result.Failure(GoalErrors.Closed);
        }

        _terms.RemoveAll(t => t.EffectiveFrom > from);
        // Mesma vigencia: altera no lugar (remover e recriar a mesma chave confunde o EF).
        if (_terms.Find(t => t.EffectiveFrom == from) is { } sameMonth)
        {
            sameMonth.Set(kind, amount, percent);
        }
        else
        {
            _terms.Add(new MonthlyGoalTerm(from, kind, amount, percent));
        }

        return Result.Success();
    }

    /// <summary>
    /// MET-2: encerra a meta a partir de um mes (deixa de valer nele e nos seguintes, continua nos anteriores).
    /// Libera o escopo para uma meta nova a partir desse mes.
    /// </summary>
    public Result Close(DateOnly fromMonth)
    {
        if (ClosedFromMonth is not null)
        {
            return Result.Failure(GoalErrors.AlreadyClosed);
        }

        var from = FirstDayOf(fromMonth);
        if (from <= StartMonth)
        {
            return Result.Failure(GoalErrors.CloseBeforeStart);
        }

        _terms.RemoveAll(t => t.EffectiveFrom >= from);
        ClosedFromMonth = from;
        OpenScope = null;
        return Result.Success();
    }

    private static Error? Validate(MonthlyGoalKind kind, decimal? amount, int? percent)
    {
        if (!Enum.IsDefined(kind))
        {
            return GoalErrors.KindInvalid;
        }

        if (kind == MonthlyGoalKind.SavePercent)
        {
            return percent is >= 1 and <= 100 ? null : GoalErrors.PercentInvalid;
        }

        return amount is { } value && value > 0 && value <= MaxAmount && decimal.Round(value, 2) == value
            ? null
            : GoalErrors.AmountInvalid;
    }

    private static DateOnly FirstDayOf(DateOnly month) => new(month.Year, month.Month, 1);
}

/// <summary>MET (ADR-0013): erros das metas.</summary>
public static class GoalErrors
{
    public static readonly Error ScopeInvalid =
        Error.Validation("goal.scope_invalid", "Escopo invalido: use 'personal' ou 'business'.");

    public static readonly Error KindInvalid =
        Error.Validation("goal.kind_invalid", "Forma invalida: use 'SaveAmount', 'SavePercent' ou 'SpendCeiling'.");

    public static readonly Error AmountInvalid = Error.Validation(
        "goal.amount_invalid", "Valor deve ser maior que zero, ate R$ 1.000.000.000, com no maximo 2 casas decimais.");

    public static readonly Error PercentInvalid =
        Error.Validation("goal.percent_invalid", "Percentual deve ser um inteiro de 1 a 100.");

    public static readonly Error MonthInvalid =
        Error.Validation("goal.month_invalid", "Mes invalido: use o formato 'yyyy-MM'.");

    public static readonly Error MonthBeforeStart =
        Error.Validation("goal.month_before_start", "O mes da alteracao nao pode ser anterior ao inicio da meta.");

    public static readonly Error CloseBeforeStart =
        Error.Validation("goal.month_before_start", "O encerramento precisa ser depois do mes de inicio da meta.");

    public static readonly Error StartBeforePreviousClose = Error.Validation(
        "goal.month_before_start", "A meta nova precisa comecar a partir do encerramento da meta anterior deste escopo.");

    public static readonly Error Closed = Error.Conflict("goal.closed", "A meta esta encerrada neste mes.");

    public static readonly Error AlreadyClosed = Error.Conflict("goal.already_closed", "A meta ja esta encerrada.");

    public static readonly Error NotFound = Error.NotFound("goal.not_found", "Meta nao encontrada.");
}
