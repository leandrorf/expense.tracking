using Expense.Tracking.Reporting.Domain.Goals;

namespace Expense.Tracking.Reporting.UnitTests;

/// <summary>MET-5 (ADR-0013, regra 6): objetivo com alvo, prazo opcional e aportes que nunca deixam o guardado negativo.</summary>
public sealed class SavingsObjectiveTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 28);

    private static SavingsObjective Emergency() =>
        SavingsObjective.Create(Guid.NewGuid(), GoalScope.Personal, "  Reserva de emergência ", 30000m, new DateOnly(2027, 12, 15), Today, Now).Value;

    [Fact]
    public void Create_TrimsTheName_AndKeepsTheDeadlineMonth()
    {
        var objective = Emergency();

        Assert.Equal(("Reserva de emergência", 30000m, (DateOnly?)new DateOnly(2027, 12, 1), 0m, false),
            (objective.Name, objective.Target, objective.Deadline, objective.Saved, objective.IsArchived));
    }

    [Theory]
    [InlineData("", 1000, "2027-01", "goal.name_invalid")]
    [InlineData("   ", 1000, "2027-01", "goal.name_invalid")]
    [InlineData("x", 0, "2027-01", "goal.target_invalid")]
    [InlineData("x", 10.555, "2027-01", "goal.target_invalid")]
    [InlineData("x", 1000000000.01, "2027-01", "goal.target_invalid")]
    [InlineData("x", 1000, "2026-10", "goal.deadline_invalid")]
    public void Create_WithInvalidValues_IsRejected(string name, double target, string deadline, string code)
    {
        var result = SavingsObjective.Create(
            Guid.NewGuid(), GoalScope.Personal, name, (decimal)target, DateOnly.ParseExact(deadline + "-01", "yyyy-MM-dd"), Today, Now);

        Assert.Equal(code, result.Error.Code);
    }

    [Fact]
    public void Create_WithTheCurrentMonthAsDeadline_OrWithoutDeadline_IsAccepted()
    {
        Assert.True(SavingsObjective.Create(Guid.NewGuid(), GoalScope.Business, "Imposto", 5000m, new DateOnly(2026, 11, 1), Today, Now).IsSuccess);
        Assert.Null(SavingsObjective.Create(Guid.NewGuid(), GoalScope.Business, "Imposto", 5000m, null, Today, Now).Value.Deadline);
        Assert.True(SavingsObjective.Create(Guid.NewGuid(), GoalScope.Business, new string('a', 60), 1m, null, Today, Now).IsSuccess);
        Assert.Equal("goal.name_invalid", SavingsObjective.Create(Guid.NewGuid(), GoalScope.Business, new string('a', 61), 1m, null, Today, Now).Error.Code);
    }

    [Fact]
    public void Contributions_AddUp_AndAWithdrawalCannotLeaveTheBalanceNegative()
    {
        var objective = Emergency();

        var deposit = objective.AddContribution(new DateOnly(2026, 11, 5), 2000m, " primeiro ", Today, Now);
        var withdrawal = objective.AddContribution(Today, -500m, null, Today, Now);
        var tooMuch = objective.AddContribution(Today, -1500.01m, null, Today, Now);

        Assert.True(deposit.IsSuccess && withdrawal.IsSuccess);
        Assert.Equal("primeiro", deposit.Value.Note);
        Assert.Equal("goal.balance_negative", tooMuch.Error.Code);
        Assert.Equal(1500m, objective.Saved);
    }

    [Theory]
    [InlineData(0, null, "goal.contribution_invalid")]
    [InlineData(10.555, null, "goal.contribution_invalid")]
    [InlineData(1000000000.01, null, "goal.contribution_invalid")]
    [InlineData(10, "121", "goal.contribution_invalid")]
    public void AddContribution_WithInvalidValues_IsRejected(double amount, string? noteLength, string code)
    {
        var note = noteLength is null ? null : new string('n', int.Parse(noteLength));

        Assert.Equal(code, Emergency().AddContribution(Today, (decimal)amount, note, Today, Now).Error.Code);
    }

    [Fact]
    public void AddContribution_InTheFuture_IsRejected()
    {
        Assert.Equal("goal.contribution_in_future", Emergency().AddContribution(Today.AddDays(1), 10m, null, Today, Now).Error.Code);
    }

    [Fact]
    public void RemoveContribution_CannotLeaveTheBalanceNegative()
    {
        var objective = Emergency();
        var deposit = objective.AddContribution(new DateOnly(2026, 11, 1), 1000m, null, Today, Now).Value;
        var withdrawal = objective.AddContribution(new DateOnly(2026, 11, 2), -800m, null, Today, Now).Value;

        Assert.Equal("goal.balance_negative", objective.RemoveContribution(deposit.Id).Error.Code);
        Assert.True(objective.RemoveContribution(withdrawal.Id).IsSuccess);
        Assert.True(objective.RemoveContribution(deposit.Id).IsSuccess);
        Assert.Equal("goal.contribution_not_found", objective.RemoveContribution(deposit.Id).Error.Code);
        Assert.Equal(0m, objective.Saved);
    }

    [Fact]
    public void Edit_KeepsAPastDeadline_ButCannotMoveItToAPastMonth()
    {
        var objective = SavingsObjective.Create(Guid.NewGuid(), GoalScope.Personal, "Viagem", 8000m, new DateOnly(2026, 11, 1), Today, Now).Value;
        var nextYear = new DateOnly(2027, 11, 28);

        // Um ano depois o prazo de nov/2026 ja passou: editar o nome mantendo o prazo continua valendo.
        Assert.True(objective.Edit("Viagem para o Chile", 9000m, new DateOnly(2026, 11, 1), nextYear).IsSuccess);
        Assert.Equal("goal.deadline_invalid", objective.Edit("Viagem", 9000m, new DateOnly(2027, 10, 1), nextYear).Error.Code);
        Assert.True(objective.Edit("Viagem", 9000m, null, nextYear).IsSuccess);
        Assert.Null(objective.Deadline);
    }

    [Fact]
    public void Archived_CannotChange_UntilReopened()
    {
        var objective = Emergency();

        objective.Archive(Now);
        objective.Archive(Now.AddDays(1));

        Assert.Equal(Now, objective.ArchivedAt);
        Assert.Equal("goal.archived", objective.AddContribution(Today, 10m, null, Today, Now).Error.Code);
        Assert.Equal("goal.archived", objective.Edit("Outro", 1m, null, Today).Error.Code);
        objective.Unarchive();
        Assert.True(objective.AddContribution(Today, 10m, null, Today, Now).IsSuccess);
    }
}
