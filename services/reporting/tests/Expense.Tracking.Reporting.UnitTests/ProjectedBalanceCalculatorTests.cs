using Expense.Tracking.Reporting.Domain.Forecast;

namespace Expense.Tracking.Reporting.UnitTests;

/// <summary>INV-13: saldo projetado no fim de cada mes, a partir do saldo informado por conta.</summary>
public sealed class ProjectedBalanceCalculatorTests
{
    private static readonly DateOnly November = new(2026, 11, 1);
    private static readonly Guid Pf = Guid.NewGuid();
    private static readonly Guid Pj = Guid.NewGuid();

    private static IReadOnlyList<MonthBalance> Calculate(
        AccountOpeningBalance[] openings,
        AccountCashMovement[]? cash = null,
        AccountInvoicePayment[]? payments = null,
        AccountInvoiceDue[]? invoices = null,
        int months = 3) =>
        ProjectedBalanceCalculator.Calculate(November, months, openings, cash ?? [], payments ?? [], invoices ?? []);

    [Fact]
    public void Balance_StartsAtTheInformedDay_AndAddsOnlyWhatComesAfterIt()
    {
        AccountOpeningBalance[] openings = [new(Pf, ForecastScopes.Personal, 1000m, new DateOnly(2026, 11, 10))];
        AccountCashMovement[] cash =
        [
            // No dia informado ou antes: ja esta no saldo.
            new(Pf, IsIncome: false, 999m, new DateOnly(2026, 11, 10)),
            new(Pf, IsIncome: true, 999m, new DateOnly(2026, 10, 1)),
            // Depois: entra no mes da data.
            new(Pf, IsIncome: false, 200m, new DateOnly(2026, 11, 11)),
            new(Pf, IsIncome: true, 5000m, new DateOnly(2026, 12, 5)),
            new(Pf, IsIncome: false, 1500m, new DateOnly(2027, 1, 31)),
        ];

        var months = Calculate(openings, cash);

        Assert.Equal([800m, 5800m, 4300m], months.Select(m => m.Personal));
        Assert.Equal(months.Select(m => m.Personal), months.Select(m => m.Total));
        Assert.All(months, m => Assert.Null(m.Business));
    }

    [Fact]
    public void Invoices_LeaveByWhatIsStillToPay_InTheDueMonth_AndPaymentsLeaveByTheirDate()
    {
        AccountOpeningBalance[] openings = [new(Pf, ForecastScopes.Personal, 3000m, new DateOnly(2026, 11, 10))];
        // Fatura de novembro: 600, ja pagos 400 no dia 15 (sai na data); faltam 200 (saem em novembro).
        AccountInvoicePayment[] payments =
        [
            new(Pf, 400m, new DateOnly(2026, 11, 15)),
            // Pago antes do saldo informado: ja esta nele.
            new(Pf, 999m, new DateOnly(2026, 11, 9)),
        ];
        AccountInvoiceDue[] invoices =
        [
            new(Pf, November, 200m),
            new(Pf, new DateOnly(2027, 1, 1), 700m),
            // Vencida antes da previsao: nao entra (sem rotativo).
            new(Pf, new DateOnly(2026, 10, 1), 999m),
        ];

        var months = Calculate(openings, payments: payments, invoices: invoices);

        Assert.Equal([2400m, 2400m, 1700m], months.Select(m => m.Personal));
    }

    [Fact]
    public void Scopes_SumTheirAccounts_AccountsWithoutBalanceStayOut_AndTotalSumsBoth()
    {
        var withoutBalance = Guid.NewGuid();
        AccountOpeningBalance[] openings =
        [
            new(Pf, ForecastScopes.Personal, 100m, new DateOnly(2026, 11, 1)),
            new(Pj, ForecastScopes.Business, -50m, new DateOnly(2026, 11, 1)),
        ];
        AccountCashMovement[] cash =
        [
            new(Pj, IsIncome: true, 1000m, new DateOnly(2026, 11, 20)),
            new(withoutBalance, IsIncome: false, 999m, new DateOnly(2026, 11, 20)),
        ];
        AccountInvoiceDue[] invoices = [new(withoutBalance, November, 999m)];

        var november = Calculate(openings, cash, invoices: invoices, months: 1).Single();

        Assert.Equal((100m, 950m, 1050m), (november.Personal, november.Business, november.Total));
    }

    [Fact]
    public void WithoutAnyInformedBalance_EveryMonthIsNull()
    {
        var months = Calculate([], [new(Pf, IsIncome: true, 10m, new DateOnly(2026, 11, 20))]);

        Assert.All(months, m => Assert.Equal(((decimal?)null, (decimal?)null, (decimal?)null), (m.Personal, m.Business, m.Total)));
    }
}
