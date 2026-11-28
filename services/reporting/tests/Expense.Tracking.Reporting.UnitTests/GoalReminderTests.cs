using Expense.Tracking.Reporting.Domain.Goals;
using Expense.Tracking.Reporting.Domain.Reminders;

namespace Expense.Tracking.Reporting.UnitTests;

/// <summary>MET-9 (ADR-0013, regra 10): aviso da meta do mes fora e do objetivo atrasado perto do prazo.</summary>
public sealed class GoalReminderTests
{
    private static readonly DateOnly Today = new(2026, 11, 21);

    private static IReadOnlyList<Reminder> Calculate(IEnumerable<MonthlyGoalCheck>? goals = null, IEnumerable<SavingsObjectiveCheck>? objectives = null) =>
        ReminderCalculator.Calculate(Today, 3, [], [], monthlyGoals: goals, objectives: objectives);

    private static SavingsPace Pace(SavingsObjectiveSituation situation, int? monthsLeft) =>
        new(2400m, monthsLeft, 800m, 300m, null, situation);

    [Fact]
    public void MonthlyGoal_Outside_IsUrgent_AndOnlyThen()
    {
        var reminders = Calculate([
            new MonthlyGoalCheck(GoalScope.Personal, MonthlyGoalKind.SaveAmount, MonthlyGoalSituation.Outside, 1000m, new MonthFigures(5000m, 4500m, 0m)),
            new MonthlyGoalCheck(GoalScope.Business, MonthlyGoalKind.SpendCeiling, MonthlyGoalSituation.Attention, 6000m, new MonthFigures(0m, 5000m, 0m)),
        ]);

        var reminder = Assert.Single(reminders);
        Assert.Equal(
            (ReminderKind.MonthlyGoal, "goal:personal:2026-11:outside", ReminderUrgency.Overdue, new DateOnly(2026, 11, 30), 500m, 1000m, "Meta do mês (PF)", "personal"),
            (reminder.Kind, reminder.Key, reminder.Urgency, reminder.DueOn, reminder.Amount, reminder.Limit, reminder.Title, reminder.Scope));
    }

    [Fact]
    public void SpendCeiling_Outside_ShowsWhatWentOut()
    {
        var reminder = Assert.Single(Calculate([
            new MonthlyGoalCheck(GoalScope.Business, MonthlyGoalKind.SpendCeiling, MonthlyGoalSituation.Outside, 6000m, new MonthFigures(0m, 5000m, 1200m)),
        ]));

        Assert.Equal((6200m, 6000m, "Meta do mês (PJ)"), (reminder.Amount, reminder.Limit, reminder.Title));
    }

    [Fact]
    public void Objective_BehindWithThreeMonthsOrLess_IsSoon()
    {
        var id = Guid.NewGuid();
        var reminders = Calculate(objectives: [
            new SavingsObjectiveCheck(id, "Viagem", GoalScope.Personal, new DateOnly(2027, 1, 1), Pace(SavingsObjectiveSituation.Behind, 3)),
            new SavingsObjectiveCheck(Guid.NewGuid(), "Carro", GoalScope.Personal, new DateOnly(2027, 2, 1), Pace(SavingsObjectiveSituation.Behind, 4)),
            new SavingsObjectiveCheck(Guid.NewGuid(), "Reserva", GoalScope.Personal, new DateOnly(2026, 12, 1), Pace(SavingsObjectiveSituation.OnTrack, 2)),
        ]);

        var reminder = Assert.Single(reminders);
        Assert.Equal(
            (ReminderKind.SavingsObjective, $"objective:{id}:2026-11:behind", ReminderUrgency.Soon, new DateOnly(2027, 1, 31), 800m, (Guid?)id, "Objetivo Viagem"),
            (reminder.Kind, reminder.Key, reminder.Urgency, reminder.DueOn, reminder.Amount, reminder.ObjectiveId, reminder.Title));
    }
}
