namespace Expense.Tracking.Reporting.Domain.Goals;

/// <summary>MET-3 (ADR-0013, regra 5): situacao da meta no mes atual ou num mes futuro.</summary>
public enum MonthlyGoalSituation
{
    /// <summary>Folga de pelo menos 20% do alvo (ou do teto).</summary>
    Inside = 1,

    /// <summary>"Ainda pode gastar" abaixo de 20% do alvo (ou do teto), sem passar dele.</summary>
    Attention = 2,

    /// <summary>Resultado previsto abaixo do alvo, ou gasto acima do teto.</summary>
    Outside = 3
}

/// <summary>MET-3: o mes num escopo, pelo mesmo calculo da previsao (entradas, saidas fora do cartao, faturas).</summary>
public sealed record MonthFigures(decimal Income, decimal Expense, decimal Invoices)
{
    /// <summary>Tudo o que sai no mes: saidas fora do cartao + faturas que vencem nele.</summary>
    public decimal Outflows => Expense + Invoices;

    /// <summary>O <c>Result</c> da previsao: entradas − saidas − faturas.</summary>
    public decimal Result => Income - Outflows;
}

/// <param name="Target">Alvo do mes: o valor a guardar, o percentual das entradas ou o teto.</param>
/// <param name="CanStillSpend">Quanto ainda cabe gastar no mes sem perder a meta; nunca negativo.</param>
public sealed record MonthlyGoalEvaluation(decimal Target, decimal CanStillSpend, MonthlyGoalSituation Situation);

/// <summary>
/// MET-3 (ADR-0013, regras 4 e 5): alvo, "ainda pode gastar" e situacao de uma vigencia num mes. Para guardar,
/// "ainda pode gastar" = resultado − alvo; para o teto, teto − saidas. Atencao abaixo de 20% do alvo (o mesmo
/// limiar de 80% dos orcamentos).
/// </summary>
public static class MonthlyGoalStatusCalculator
{
    /// <summary>Percentual do alvo abaixo do qual a folga vira atencao.</summary>
    public const decimal AttentionSlackShare = 0.20m;

    public static MonthlyGoalEvaluation Evaluate(MonthlyGoalTerm term, MonthFigures figures)
    {
        var target = TargetOf(term, figures);
        var slack = term.Kind == MonthlyGoalKind.SpendCeiling
            ? target - figures.Outflows
            : figures.Result - target;

        var situation = slack < 0m
            ? MonthlyGoalSituation.Outside
            : slack < target * AttentionSlackShare
                ? MonthlyGoalSituation.Attention
                : MonthlyGoalSituation.Inside;
        return new MonthlyGoalEvaluation(target, Math.Max(slack, 0m), situation);
    }

    /// <summary>O alvo do mes; no percentual, arredondado em 2 casas com o meio para longe do zero.</summary>
    public static decimal TargetOf(MonthlyGoalTerm term, MonthFigures figures) => term.Kind switch
    {
        MonthlyGoalKind.SavePercent =>
            decimal.Round(figures.Income * term.Percent!.Value / 100m, 2, MidpointRounding.AwayFromZero),
        _ => term.Amount!.Value
    };
}
