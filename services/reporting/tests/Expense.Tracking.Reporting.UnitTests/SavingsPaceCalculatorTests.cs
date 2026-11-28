using Expense.Tracking.Reporting.Domain.Goals;

namespace Expense.Tracking.Reporting.UnitTests;

/// <summary>MET-6 (ADR-0013, regra 7): falta, precisa por mes, ritmo, chegada e situacao, com o exemplo da ADR.</summary>
public sealed class SavingsPaceCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 28);
    private static readonly DateOnly LongAgo = new(2026, 1, 10);

    private static SavingsObjective Objective(decimal target = 30000m, DateOnly? deadline = null, params (string Date, decimal Amount)[] contributions)
    {
        var objective = SavingsObjective.Create(Guid.NewGuid(), GoalScope.Personal, "Reserva", target, deadline, Today, Now).Value;
        foreach (var (date, amount) in contributions)
        {
            Assert.True(objective.AddContribution(DateOnly.Parse(date), amount, null, Today, Now).IsSuccess);
        }

        return objective;
    }

    [Fact]
    public void AdrExample_Behind_ArrivesInNovember2028()
    {
        // R$ 30.000 ate dez/2027; R$ 3.000 guardados antes e R$ 1.000 em cada um dos ultimos 3 meses.
        var objective = Objective(30000m, new DateOnly(2027, 12, 1),
            ("2026-05-10", 3000m), ("2026-09-05", 1000m), ("2026-10-05", 1000m), ("2026-11-05", 1000m));

        var pace = SavingsPaceCalculator.Evaluate(objective, Today, LongAgo);

        Assert.Equal(
            new SavingsPace(24000m, 14, 1714.29m, 1000m, new DateOnly(2028, 11, 1), SavingsObjectiveSituation.Behind),
            pace);
    }

    [Fact]
    public void FastEnoughPace_IsOnTrack()
    {
        var objective = Objective(6000m, new DateOnly(2027, 2, 1), ("2026-10-01", 1500m), ("2026-11-01", 1500m));

        var pace = SavingsPaceCalculator.Evaluate(objective, Today, LongAgo);

        // Falta 3.000 em 4 meses (nov a fev) = 750; ritmo 3.000 / 3 = 1.000; chega em nov + 3 = fev/2027.
        Assert.Equal(new SavingsPace(3000m, 4, 750m, 1000m, new DateOnly(2027, 2, 1), SavingsObjectiveSituation.OnTrack), pace);
    }

    [Fact]
    public void NewObjective_DividesThePaceByTheMonthsSinceCreation()
    {
        var objective = Objective(1200m, null, ("2026-11-10", 300m));

        var thisMonth = SavingsPaceCalculator.Evaluate(objective, Today, new DateOnly(2026, 11, 2));
        var lastMonth = SavingsPaceCalculator.Evaluate(objective, Today, new DateOnly(2026, 10, 20));

        Assert.Equal((300m, (DateOnly?)new DateOnly(2027, 2, 1)), (thisMonth.Pace, thisMonth.ExpectedArrival));
        Assert.Equal(150m, lastMonth.Pace);
        // Sem prazo: so ritmo e chegada.
        Assert.Equal(((int?)null, (decimal?)null, SavingsObjectiveSituation.OnTrack), (thisMonth.MonthsLeft, thisMonth.NeededPerMonth, thisMonth.Situation));
    }

    [Fact]
    public void ContributionsOlderThanTheWindow_AndWithdrawals_CountAsExpected()
    {
        var objective = Objective(10000m, null, ("2026-08-31", 5000m), ("2026-09-01", 900m), ("2026-11-20", -300m));

        var pace = SavingsPaceCalculator.Evaluate(objective, Today, LongAgo);

        Assert.Equal(200m, pace.Pace);
    }

    [Fact]
    public void WithoutContributions_ThereIsNoArrival()
    {
        var pace = SavingsPaceCalculator.Evaluate(Objective(1000m, new DateOnly(2026, 12, 1)), Today, LongAgo);

        Assert.Equal(new SavingsPace(1000m, 2, 500m, 0m, null, SavingsObjectiveSituation.Behind), pace);
    }

    [Fact]
    public void Reached_HasNothingMissing_NorNeeded()
    {
        var objective = Objective(1000m, new DateOnly(2027, 6, 1), ("2026-11-01", 1200m));

        var pace = SavingsPaceCalculator.Evaluate(objective, Today, LongAgo);

        Assert.Equal((0m, (decimal?)null, (DateOnly?)null, SavingsObjectiveSituation.Reached),
            (pace.Missing, pace.NeededPerMonth, pace.ExpectedArrival, pace.Situation));
    }

    [Fact]
    public void DeadlinePassed_WithoutReaching()
    {
        var objective = Objective(1000m, new DateOnly(2026, 11, 1), ("2026-11-01", 400m));
        var december = new DateOnly(2026, 12, 3);

        var inTheMonth = SavingsPaceCalculator.Evaluate(objective, Today, LongAgo);
        var after = SavingsPaceCalculator.Evaluate(objective, december, LongAgo);

        Assert.Equal((1, (decimal?)600m, SavingsObjectiveSituation.Behind), (inTheMonth.MonthsLeft, inTheMonth.NeededPerMonth, inTheMonth.Situation));
        Assert.Equal(((int?)null, (decimal?)null, SavingsObjectiveSituation.DeadlinePassed), (after.MonthsLeft, after.NeededPerMonth, after.Situation));
    }
}
