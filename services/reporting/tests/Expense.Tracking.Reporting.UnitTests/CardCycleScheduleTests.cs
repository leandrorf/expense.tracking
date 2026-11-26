using Expense.Tracking.Reporting.Domain.Invoices;

namespace Expense.Tracking.Reporting.UnitTests;

/// <summary>
/// CYC-4 (ADR-0012, decisoes 2 e 6): periodos encadeados com ciclos de vigencia. Os exemplos sao os mesmos do
/// accounts (CardCycleTests), para as duas copias da regra nao divergirem.
/// </summary>
public sealed class CardCycleScheduleTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    private static CardCycleSchedule Schedule(params CardCycleTerm[] terms) => new(terms);

    private static InvoicePeriod Period(string due, string start, string closing, string dueDate) =>
        new(D(due), D(start), D(closing), D(dueDate));

    // ---------- Exemplos da regra (iguais no accounts) ----------

    [Fact]
    public void SingleCycle_IsTheMonthlyCycle()
    {
        var s = Schedule(new CardCycleTerm(null, 10, 17));

        Assert.Equal(Period("2026-12-01", "2026-11-11", "2026-12-10", "2026-12-17"), s.PeriodOf(D("2026-12-15")));
        Assert.Equal((D("2026-11-01"), D("2026-12-01")), (s.DueMonthOf(D("2026-11-10")), s.DueMonthOf(D("2026-11-11"))));
    }

    [Fact]
    public void ChangeToALaterClosing_MakesAShortTransitionInvoice()
    {
        var s = Schedule(new CardCycleTerm(null, 10, 17), new CardCycleTerm(D("2027-03-01"), 25, 5));

        Assert.Equal(Period("2027-02-01", "2027-01-11", "2027-02-10", "2027-02-17"), s.PeriodOf(D("2027-02-01")));
        Assert.Equal(Period("2027-03-01", "2027-02-11", "2027-02-25", "2027-03-05"), s.PeriodOf(D("2027-03-01")));
        Assert.Equal(Period("2027-04-01", "2027-02-26", "2027-03-25", "2027-04-05"), s.PeriodOf(D("2027-04-01")));
        Assert.Equal([D("2027-02-01"), D("2027-03-01"), D("2027-03-01"), D("2027-04-01")],
            new[] { D("2027-02-10"), D("2027-02-11"), D("2027-02-25"), D("2027-02-26") }.Select(s.DueMonthOf));
    }

    [Fact]
    public void ChangeToAnEarlierClosing_MakesALongTransitionInvoice()
    {
        var s = Schedule(new CardCycleTerm(null, 25, 5), new CardCycleTerm(D("2027-03-01"), 5, 15));

        Assert.Equal(Period("2027-03-01", "2027-01-26", "2027-03-05", "2027-03-15"), s.PeriodOf(D("2027-03-01")));
        Assert.Equal(D("2027-03-01"), s.DueMonthOf(D("2027-02-20")));
    }

    [Fact]
    public void AnOldInvoiceClosingAfterTheFirstNewClosing_DoesNotExist_AndTheDueMonthSkips()
    {
        var s = Schedule(new CardCycleTerm(null, 5, 15), new CardCycleTerm(D("2027-04-01"), 3, 1));

        Assert.True(s.Exists(D("2027-02-01")));
        Assert.False(s.Exists(D("2027-03-01")));
        Assert.Equal(Period("2027-04-01", "2027-02-06", "2027-03-03", "2027-04-01"), s.PeriodOf(D("2027-04-01")));
        Assert.Equal([D("2027-02-01"), D("2027-04-01"), D("2027-04-01"), D("2027-05-01")],
            new[] { D("2027-02-05"), D("2027-02-06"), D("2027-03-03"), D("2027-03-04") }.Select(s.DueMonthOf));
        Assert.Equal(D("2027-04-01"), s.NextDueMonth(D("2027-02-01")));
    }

    // ---------- Faturas com a troca ----------

    [Fact]
    public void Invoices_KeepTheClosedOnes_AndInstallmentsSkipTheMissingMonth()
    {
        // Fecha 5/vence 15; a partir de abr/2027, fecha 3 e vence 1 do mes seguinte (marco some).
        var s = Schedule(new CardCycleTerm(null, 5, 15), new CardCycleTerm(D("2027-04-01"), 3, 1));
        var notebook = Guid.NewGuid();
        CardCharge[] charges =
        [
            new(Guid.NewGuid(), "Mercado", 100m, D("2027-01-20")),        // fev (fechou 05/02)
            new(notebook, "Eletronicos", 300m, D("2027-01-25"), Installments: 3), // fev, abr, mai
            new(Guid.NewGuid(), "Farmacia", 40m, D("2027-02-20")),         // abr (06/02 a 03/03)
        ];

        var invoices = InvoiceCalculator.Calculate(s, charges, [new InvoicePayment(D("2027-02-01"), 200m)], D("2027-02-10"));

        Assert.Equal([D("2027-02-01"), D("2027-04-01"), D("2027-05-01")], invoices.Select(i => i.Period.DueMonth));
        Assert.Equal([200m, 140m, 100m], invoices.Select(i => i.Total));
        Assert.Equal((200m, InvoiceStatus.Paid), (invoices[0].Paid, invoices[0].Status));
        Assert.Equal([1, 2, 3], invoices.SelectMany(i => i.Items).Where(i => i.TransactionId == notebook).Select(i => i.Installment));
    }
}
