using Expense.Tracking.Reporting.Domain.Forecast;

namespace Expense.Tracking.Reporting.UnitTests;

/// <summary>INV-8 (ADR-0005, decisao 8): regra da previsao de caixa mes a mes, por escopo.</summary>
public sealed class CashForecastCalculatorTests
{
    private static readonly DateOnly November = new(2026, 11, 1);

    [Fact]
    public void Months_StartAtTheGivenMonth_AndSumCashByDate_AndInvoicesByDueMonth()
    {
        CashMovement[] cash =
        [
            new(ForecastScopes.Personal, IsIncome: true, 5000m, new DateOnly(2026, 12, 5)),
            new(ForecastScopes.Personal, IsIncome: false, 1200m, new DateOnly(2026, 12, 10)),
            new(ForecastScopes.Business, IsIncome: false, 300m, new DateOnly(2026, 11, 30)),
            // Fora da janela (antes e depois): nao entra.
            new(ForecastScopes.Personal, IsIncome: false, 999m, new DateOnly(2026, 10, 31)),
            new(ForecastScopes.Personal, IsIncome: false, 999m, new DateOnly(2027, 2, 1)),
        ];
        ForecastInvoice[] invoices =
        [
            new(ForecastScopes.Personal, new DateOnly(2026, 12, 1), 800m, 0m, 800m),
            new(ForecastScopes.Business, new DateOnly(2026, 12, 1), 500m, 500m, 0m),
            new(ForecastScopes.Personal, new DateOnly(2027, 3, 1), 999m, 0m, 999m),
        ];

        var months = CashForecastCalculator.Calculate(November, 3, cash, invoices);

        Assert.Equal([November, new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 1)], months.Select(m => m.Month));
        Assert.Equal(new MonthFlow(0m, 300m, 0m, 0m, 0m), months[0].Business);
        Assert.Equal(MonthFlow.Empty, months[0].Personal);
        var december = months[1];
        Assert.Equal(new MonthFlow(5000m, 1200m, 800m, 0m, 800m), december.Personal);
        Assert.Equal(3000m, december.Personal.Result);
        // Fatura paga: total entra no resultado, nada falta pagar.
        Assert.Equal(new MonthFlow(0m, 0m, 500m, 500m, 0m), december.Business);
        Assert.Equal(-500m, december.Business.Result);
        Assert.Equal(new MonthFlow(5000m, 1200m, 1300m, 500m, 800m), december.Total);
        Assert.Equal(MonthFlow.Empty, months[2].Total);
    }

    [Fact]
    public void UnknownScope_CountsOnlyInTheTotal()
    {
        var months = CashForecastCalculator.Calculate(
            November, 1, [new CashMovement(null, IsIncome: false, 40m, new DateOnly(2026, 11, 2))],
            [new ForecastInvoice(null, November, 60m, 0m, 60m)]);

        Assert.Equal(MonthFlow.Empty, months[0].Personal);
        Assert.Equal(MonthFlow.Empty, months[0].Business);
        Assert.Equal(new MonthFlow(0m, 40m, 60m, 0m, 60m), months[0].Total);
    }

    [Fact]
    public void FirstMonth_IsNormalizedToItsFirstDay()
    {
        var months = CashForecastCalculator.Calculate(new DateOnly(2026, 11, 21), 1,
            [new CashMovement(ForecastScopes.Personal, IsIncome: true, 10m, new DateOnly(2026, 11, 1))], []);

        Assert.Equal(10m, Assert.Single(months).Personal.Income);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void MonthsOutOfRange_Throw(int months) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CashForecastCalculator.Calculate(November, months, [], []));
}
