using Expense.Tracking.Reporting.Domain.Forecast;

namespace Expense.Tracking.Reporting.UnitTests;

/// <summary>RJT-8 (ADR-0010, decisao 8): o reajuste estimado na previsao.</summary>
public sealed class RecurrenceEstimateTests
{
    private static readonly DateOnly StartsOn = new(2026, 1, 10);
    private static readonly RecurrenceAdjustmentEstimate FivePercentInMarch = new(3, 5m, NoReduction: true);

    private static EstimatedAdjustment? Estimate(
        IReadOnlyList<RecurrenceAmountFrom> amounts, DateOnly today, RecurrenceAdjustmentEstimate? adjustment = null,
        string frequency = "Monthly", DateOnly? startsOn = null, decimal fallback = 2000m) =>
        RecurrenceAmounts.Estimate(amounts, fallback, frequency, startsOn ?? StartsOn, today, adjustment ?? FivePercentInMarch);

    [Fact]
    public void Estimate_TakesTheLatestAnniversaryWithoutAmount_OrTheNextOne()
    {
        var first = new RecurrenceAmountFrom("2026-01", 2000m);

        // Marco de 2026 passou sem reajuste: ele e o estimado (vale de hoje em diante).
        Assert.Equal(new EstimatedAdjustment("2026-03", 2000m, 2100m), Estimate([first], new DateOnly(2026, 11, 24)));

        // Com a vigencia de marco/2026, o estimado e o de marco/2027, sobre ela.
        Assert.Equal(new EstimatedAdjustment("2027-03", 2080m, 2184m),
            Estimate([first, new("2026-03", 2080m)], new DateOnly(2026, 11, 24)));

        // Antes do primeiro aniversario depois do inicio: o proximo.
        Assert.Equal("2026-03", Estimate([first], new DateOnly(2026, 2, 1))!.FromPeriod);

        // O aniversario no periodo do inicio nao conta.
        Assert.Equal("2027-03", Estimate([new("2026-03", 2000m)], new DateOnly(2026, 5, 1), startsOn: new DateOnly(2026, 3, 1))!.FromPeriod);

        // Anual: o periodo e o ano.
        Assert.Equal(new EstimatedAdjustment("2027", 1200m, 1260m), Estimate([new("2026", 1200m)], new DateOnly(2026, 5, 1),
            new RecurrenceAdjustmentEstimate(1, 5m, true), "Yearly", new DateOnly(2026, 1, 15), 1200m));

        Assert.Null(RecurrenceAmounts.Estimate([first], 2000m, "Monthly", StartsOn, new DateOnly(2026, 11, 24), null));
    }

    [Fact]
    public void Estimate_RoundsHalfUp_AndNoReductionKeepsTheBase()
    {
        Assert.Equal(1100.06m, Estimate([new("2026-01", 1000.05m)], new DateOnly(2026, 4, 1), new(3, 10m, true), fallback: 1000.05m)!.Amount);
        Assert.Equal(2000m, Estimate([new("2026-01", 2000m)], new DateOnly(2026, 4, 1), new(3, -2m, true))!.Amount);
        Assert.Equal(1960m, Estimate([new("2026-01", 2000m)], new DateOnly(2026, 4, 1), new(3, -2m, false))!.Amount);
    }

    [Fact]
    public void ForForecast_UsesTheEstimateFromItsPeriod_UntilALaterAmountStarts()
    {
        IReadOnlyList<RecurrenceAmountFrom> amounts = [new("2026-01", 2000m), new("2027-06", 2500m)];
        var estimate = new EstimatedAdjustment("2027-03", 2000m, 2100m);

        Assert.Equal(new ForecastAmount(2000m, 0m), RecurrenceAmounts.ForForecast(amounts, 2000m, "2027-02", estimate));
        Assert.Equal(new ForecastAmount(2100m, 100m), RecurrenceAmounts.ForForecast(amounts, 2000m, "2027-03", estimate));
        Assert.Equal(new ForecastAmount(2100m, 100m), RecurrenceAmounts.ForForecast(amounts, 2000m, "2027-05", estimate));
        Assert.Equal(new ForecastAmount(2500m, 0m), RecurrenceAmounts.ForForecast(amounts, 2000m, "2027-06", estimate));
        Assert.Equal(new ForecastAmount(2000m, 0m), RecurrenceAmounts.ForForecast(amounts, 2000m, "2027-04", null));
    }
}
