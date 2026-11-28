using Expense.Tracking.Reporting.Domain.Goals;

namespace Expense.Tracking.Reporting.UnitTests;

/// <summary>MET-3 (ADR-0013, regras 4 e 5): alvo, "ainda pode gastar" e situacao, com os exemplos da ADR.</summary>
public sealed class MonthlyGoalStatusCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);

    private static MonthlyGoalTerm Term(MonthlyGoalKind kind, decimal? amount = null, int? percent = null) =>
        MonthlyGoal.Create(Guid.NewGuid(), GoalScope.Personal, kind, amount, percent, new DateOnly(2026, 11, 1), Now).Value.Terms[0];

    [Theory]
    // Exemplo da ADR: guardar 1.000 com 5.000 de entradas e 3.500 de saidas + faturas.
    [InlineData(3000, 500, 500, MonthlyGoalSituation.Inside)]
    // Um gasto de 350: sobra 150, 15% do alvo.
    [InlineData(3350, 500, 150, MonthlyGoalSituation.Attention)]
    // Folga de exatamente 20% do alvo ainda e "dentro".
    [InlineData(3300, 500, 200, MonthlyGoalSituation.Inside)]
    // Meta atingida exatamente: nada mais para gastar, ainda sem perder a meta.
    [InlineData(3500, 500, 0, MonthlyGoalSituation.Attention)]
    [InlineData(3600, 500, 0, MonthlyGoalSituation.Outside)]
    public void SaveAmount_ComparesTheResultWithTheAmount(decimal expense, decimal invoices, decimal canStillSpend, MonthlyGoalSituation situation)
    {
        var evaluation = MonthlyGoalStatusCalculator.Evaluate(Term(MonthlyGoalKind.SaveAmount, 1000m), new MonthFigures(5000m, expense, invoices));

        Assert.Equal((1000m, canStillSpend, situation), (evaluation.Target, evaluation.CanStillSpend, evaluation.Situation));
    }

    [Fact]
    public void SavePercent_UsesThePercentOfTheMonthIncome_RoundedAwayFromZero()
    {
        var evaluation = MonthlyGoalStatusCalculator.Evaluate(Term(MonthlyGoalKind.SavePercent, percent: 20), new MonthFigures(4321.23m, 3000m, 0m));

        // 20% de 4.321,23 = 864,246 -> 864,25; resultado 1.321,23; sobra 456,98.
        Assert.Equal((864.25m, 456.98m, MonthlyGoalSituation.Inside), (evaluation.Target, evaluation.CanStillSpend, evaluation.Situation));
    }

    [Fact]
    public void SavePercent_WithoutIncome_HasATargetOfZero_AndASpendingMonthIsOutside()
    {
        var evaluation = MonthlyGoalStatusCalculator.Evaluate(Term(MonthlyGoalKind.SavePercent, percent: 20), new MonthFigures(0m, 100m, 0m));

        Assert.Equal((0m, 0m, MonthlyGoalSituation.Outside), (evaluation.Target, evaluation.CanStillSpend, evaluation.Situation));
    }

    [Theory]
    [InlineData(3800, 1000, 1200, MonthlyGoalSituation.Inside)]
    [InlineData(4500, 700, 800, MonthlyGoalSituation.Attention)]
    // Exemplo da historia: teto de 6.000 com 6.200 de saidas + faturas.
    [InlineData(5000, 1200, 0, MonthlyGoalSituation.Outside)]
    public void SpendCeiling_ComparesTheOutflowsWithTheCeiling_WhateverTheIncome(
        decimal expense, decimal invoices, decimal canStillSpend, MonthlyGoalSituation situation)
    {
        var evaluation = MonthlyGoalStatusCalculator.Evaluate(Term(MonthlyGoalKind.SpendCeiling, 6000m), new MonthFigures(0m, expense, invoices));

        Assert.Equal((6000m, canStillSpend, situation), (evaluation.Target, evaluation.CanStillSpend, evaluation.Situation));
    }
}
