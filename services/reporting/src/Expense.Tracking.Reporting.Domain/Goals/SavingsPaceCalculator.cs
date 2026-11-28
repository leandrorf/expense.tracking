namespace Expense.Tracking.Reporting.Domain.Goals;

/// <summary>MET-6 (ADR-0013, regra 7): situacao de um objetivo.</summary>
public enum SavingsObjectiveSituation
{
    /// <summary>Ritmo igual ou acima do necessario (ou sem prazo e ainda nao atingido).</summary>
    OnTrack = 1,

    /// <summary>Ritmo abaixo do que precisa por mes para chegar no prazo.</summary>
    Behind = 2,

    /// <summary>Guardado igual ou acima do alvo.</summary>
    Reached = 3,

    /// <summary>O prazo passou sem atingir o alvo.</summary>
    DeadlinePassed = 4
}

/// <param name="Missing">Alvo − guardado, nunca negativo.</param>
/// <param name="MonthsLeft">Meses do mes atual ate o do prazo, contando os dois; nulo sem prazo ou com prazo vencido.</param>
/// <param name="NeededPerMonth">Falta ÷ meses restantes; nulo sem prazo, com prazo vencido ou atingido.</param>
/// <param name="Pace">Media mensal dos aportes liquidos dos ultimos 3 meses (contando o atual).</param>
/// <param name="ExpectedArrival">Mes (dia 1) em que o ritmo atual chega ao alvo; nulo se atingido ou ritmo ≤ 0.</param>
public sealed record SavingsPace(
    decimal Missing, int? MonthsLeft, decimal? NeededPerMonth, decimal Pace, DateOnly? ExpectedArrival, SavingsObjectiveSituation Situation);

/// <summary>
/// MET-6 (ADR-0013, regra 7): falta, precisa por mes, ritmo, previsao de chegada e situacao de um objetivo.
/// O ritmo soma os aportes (retiradas negativas) do primeiro dia de dois meses atras ate hoje e divide por 3;
/// objetivo mais novo divide pelos meses desde a criacao (no minimo 1). Valores em 2 casas, meio para longe do zero.
/// </summary>
public static class SavingsPaceCalculator
{
    public const int PaceMonths = 3;

    /// <param name="today">Hoje no fuso do app.</param>
    /// <param name="createdOn">Dia da criacao do objetivo no fuso do app.</param>
    public static SavingsPace Evaluate(SavingsObjective objective, DateOnly today, DateOnly createdOn)
    {
        var current = FirstDayOf(today);
        var missing = Math.Max(objective.Target - objective.Saved, 0m);

        var windowStart = current.AddMonths(-(PaceMonths - 1));
        var monthsSinceCreation = MonthsBetween(FirstDayOf(createdOn), current) + 1;
        var divisor = Math.Clamp(monthsSinceCreation, 1, PaceMonths);
        var net = objective.Contributions.Where(c => c.OccurredOn >= windowStart && c.OccurredOn <= today).Sum(c => c.Amount);
        var pace = Round(net / divisor);

        int? monthsLeft = objective.Deadline is { } deadline && deadline >= current
            ? MonthsBetween(current, deadline) + 1
            : null;
        var reached = missing == 0m;
        decimal? needed = reached || monthsLeft is null ? null : Round(missing / monthsLeft.Value);
        DateOnly? arrival = reached || pace <= 0m ? null : current.AddMonths((int)Math.Ceiling(missing / pace));

        var situation = reached
            ? SavingsObjectiveSituation.Reached
            : objective.Deadline is { } d && d < current
                ? SavingsObjectiveSituation.DeadlinePassed
                : needed is { } n && pace < n
                    ? SavingsObjectiveSituation.Behind
                    : SavingsObjectiveSituation.OnTrack;
        return new SavingsPace(missing, monthsLeft, needed, pace, arrival, situation);
    }

    private static int MonthsBetween(DateOnly from, DateOnly to) => (to.Year - from.Year) * 12 + to.Month - from.Month;

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static DateOnly FirstDayOf(DateOnly day) => new(day.Year, day.Month, 1);
}
