using Expense.Tracking.Reporting.Domain.Forecast;

namespace Expense.Tracking.Reporting.UnitTests;

/// <summary>REC-4: a copia da agenda no reporting segue as mesmas regras do transactions (ADR-0008, decisao 3).</summary>
public sealed class RecurrenceScheduleTests
{
    [Fact]
    public void Monthly_UsesTheStartDay_TheLastDayOfShorterMonths_AndStaysInsideStartEndAndWindow()
    {
        var dates = RecurrenceSchedule.Occurrences(
                RecurrenceSchedule.Monthly, new DateOnly(2027, 1, 31), new DateOnly(2027, 4, 29),
                new DateOnly(2026, 12, 1), new DateOnly(2027, 12, 31))
            .ToList();

        // Abril cai em 30/04, depois do fim (29/04).
        Assert.Equal(
            [("2027-01", new DateOnly(2027, 1, 31)), ("2027-02", new DateOnly(2027, 2, 28)), ("2027-03", new DateOnly(2027, 3, 31))],
            dates.Select(o => (o.Period, o.ScheduledOn)));
    }

    [Fact]
    public void Monthly_FromTheMiddleOfTheMonth_SkipsTheDateThatAlreadyPassed()
    {
        var dates = RecurrenceSchedule.Occurrences(
                RecurrenceSchedule.Monthly, new DateOnly(2026, 11, 5), null, new DateOnly(2026, 11, 21), new DateOnly(2027, 1, 31))
            .Select(o => o.Period);

        Assert.Equal(["2026-12", "2027-01"], dates);
    }

    [Fact]
    public void Yearly_RepeatsOnTheStartDayAndMonth_AndFeb29FallsOn28()
    {
        var dates = RecurrenceSchedule.Occurrences(
                RecurrenceSchedule.Yearly, new DateOnly(2028, 2, 29), null, new DateOnly(2028, 3, 1), new DateOnly(2030, 12, 31))
            .ToList();

        Assert.Equal(
            [("2029", new DateOnly(2029, 2, 28)), ("2030", new DateOnly(2030, 2, 28))],
            dates.Select(o => (o.Period, o.ScheduledOn)));
    }
}
