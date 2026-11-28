using Expense.Tracking.Reporting.Domain.Goals;

namespace Expense.Tracking.Reporting.UnitTests;

/// <summary>MET-2 (ADR-0013, regra 3): meta mensal por escopo, com vigencias e encerramento.</summary>
public sealed class MonthlyGoalTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);

    private static MonthlyGoal SaveThousandFromDecember() =>
        MonthlyGoal.Create(Guid.NewGuid(), GoalScope.Personal, MonthlyGoalKind.SaveAmount, 1000m, null, new DateOnly(2026, 12, 15), Now).Value;

    [Fact]
    public void Create_StartsOnTheFirstDayOfTheMonth_WithOneTerm()
    {
        var goal = SaveThousandFromDecember();

        Assert.Equal(new DateOnly(2026, 12, 1), goal.StartMonth);
        Assert.Equal(GoalScope.Personal, goal.OpenScope);
        Assert.Null(goal.ClosedFromMonth);
        var term = Assert.Single(goal.Terms);
        Assert.Equal((new DateOnly(2026, 12, 1), MonthlyGoalKind.SaveAmount, (decimal?)1000m, (int?)null),
            (term.EffectiveFrom, term.Kind, term.Amount, term.Percent));
    }

    [Fact]
    public void Change_ToPercentFromMarch_KeepsTheAmountFromDecemberToFebruary()
    {
        var goal = SaveThousandFromDecember();

        var result = goal.Change(MonthlyGoalKind.SavePercent, null, 20, new DateOnly(2027, 3, 1));

        Assert.True(result.IsSuccess);
        Assert.Null(goal.TermFor(new DateOnly(2026, 11, 1)));
        Assert.Equal(1000m, goal.TermFor(new DateOnly(2026, 12, 1))!.Amount);
        Assert.Equal(MonthlyGoalKind.SaveAmount, goal.TermFor(new DateOnly(2027, 2, 1))!.Kind);
        var march = goal.TermFor(new DateOnly(2027, 3, 1))!;
        Assert.Equal((MonthlyGoalKind.SavePercent, (decimal?)null, (int?)20), (march.Kind, march.Amount, march.Percent));
        Assert.Equal(20, goal.TermFor(new DateOnly(2030, 1, 1))!.Percent);
    }

    [Fact]
    public void Change_BeforeLaterTerms_ReplacesThem_AndTheSameMonthIsChangedInPlace()
    {
        var goal = SaveThousandFromDecember();
        goal.Change(MonthlyGoalKind.SavePercent, null, 20, new DateOnly(2027, 3, 1));

        goal.Change(MonthlyGoalKind.SpendCeiling, 6000m, null, new DateOnly(2027, 1, 1));
        goal.Change(MonthlyGoalKind.SpendCeiling, 5500m, null, new DateOnly(2027, 1, 1));

        Assert.Equal(
            [(new DateOnly(2026, 12, 1), MonthlyGoalKind.SaveAmount, (decimal?)1000m), (new DateOnly(2027, 1, 1), MonthlyGoalKind.SpendCeiling, 5500m)],
            goal.Terms.Select(t => (t.EffectiveFrom, t.Kind, t.Amount)).ToArray());
    }

    [Fact]
    public void Change_BeforeTheStart_IsRejected()
    {
        var result = SaveThousandFromDecember().Change(MonthlyGoalKind.SaveAmount, 500m, null, new DateOnly(2026, 11, 1));

        Assert.Equal("goal.month_before_start", result.Error.Code);
    }

    [Fact]
    public void Close_FromAMonth_StopsTheGoalThere_AndFreesTheScope()
    {
        var goal = SaveThousandFromDecember();
        goal.Change(MonthlyGoalKind.SaveAmount, 1200m, null, new DateOnly(2027, 4, 1));

        var result = goal.Close(new DateOnly(2027, 3, 1));

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2027, 3, 1), goal.ClosedFromMonth);
        Assert.Null(goal.OpenScope);
        Assert.Equal(1000m, goal.TermFor(new DateOnly(2027, 2, 1))!.Amount);
        Assert.Null(goal.TermFor(new DateOnly(2027, 3, 1)));
        Assert.Single(goal.Terms);
        Assert.Equal("goal.already_closed", goal.Close(new DateOnly(2027, 5, 1)).Error.Code);
        Assert.Equal("goal.closed", goal.Change(MonthlyGoalKind.SaveAmount, 1m, null, new DateOnly(2027, 3, 1)).Error.Code);
    }

    [Fact]
    public void Close_OnOrBeforeTheStart_IsRejected()
    {
        Assert.Equal("goal.month_before_start", SaveThousandFromDecember().Close(new DateOnly(2026, 12, 1)).Error.Code);
    }

    [Theory]
    [InlineData(MonthlyGoalKind.SaveAmount, null, null, "goal.amount_invalid")]
    [InlineData(MonthlyGoalKind.SaveAmount, 0.0, null, "goal.amount_invalid")]
    [InlineData(MonthlyGoalKind.SpendCeiling, -1.0, null, "goal.amount_invalid")]
    [InlineData(MonthlyGoalKind.SpendCeiling, 10.555, null, "goal.amount_invalid")]
    [InlineData(MonthlyGoalKind.SaveAmount, 1000000000.01, null, "goal.amount_invalid")]
    [InlineData(MonthlyGoalKind.SavePercent, null, null, "goal.percent_invalid")]
    [InlineData(MonthlyGoalKind.SavePercent, null, 0, "goal.percent_invalid")]
    [InlineData(MonthlyGoalKind.SavePercent, 1000.0, 101, "goal.percent_invalid")]
    [InlineData((MonthlyGoalKind)9, 1000.0, null, "goal.kind_invalid")]
    public void Create_WithInvalidValues_IsRejected(MonthlyGoalKind kind, double? amount, int? percent, string code)
    {
        var result = MonthlyGoal.Create(
            Guid.NewGuid(), GoalScope.Business, kind, amount is null ? null : (decimal)amount, percent, new DateOnly(2026, 12, 1), Now);

        Assert.Equal(code, result.Error.Code);
    }

    [Fact]
    public void Create_InPercent_IgnoresTheAmount_AndInAmount_IgnoresThePercent()
    {
        var percent = MonthlyGoal.Create(Guid.NewGuid(), GoalScope.Personal, MonthlyGoalKind.SavePercent, 99m, 20, new DateOnly(2026, 12, 1), Now).Value;
        var ceiling = MonthlyGoal.Create(Guid.NewGuid(), GoalScope.Personal, MonthlyGoalKind.SpendCeiling, 6000m, 20, new DateOnly(2026, 12, 1), Now).Value;

        Assert.Equal(((decimal?)null, (int?)20), (percent.Terms[0].Amount, percent.Terms[0].Percent));
        Assert.Equal(((decimal?)6000m, (int?)null), (ceiling.Terms[0].Amount, ceiling.Terms[0].Percent));
    }
}
