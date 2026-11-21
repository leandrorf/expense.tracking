using Expense.Tracking.Reporting.Domain.Invoices;

namespace Expense.Tracking.Reporting.UnitTests;

/// <summary>INV-3 (ADR-0005, decisoes 4, 5 e 7): ciclo do cartao, parcelas e situacao da fatura.</summary>
public sealed class InvoiceCalculatorTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    // ---------- Ciclo ----------

    [Theory]
    // Fecha dia 10, vence dia 17 (vencimento no mesmo mes do fechamento).
    [InlineData(10, 17, "2026-11-10", "2026-11-01")] // no dia do fechamento: fatura deste mes
    [InlineData(10, 17, "2026-11-11", "2026-12-01")] // um dia depois: fatura do mes seguinte
    [InlineData(10, 17, "2026-11-01", "2026-11-01")]
    [InlineData(10, 17, "2026-12-15", "2027-01-01")] // atravessa o ano
    // Fecha dia 25, vence dia 5 (vencimento no mes seguinte ao fechamento).
    [InlineData(25, 5, "2026-11-20", "2026-12-01")]
    [InlineData(25, 5, "2026-11-25", "2026-12-01")]
    [InlineData(25, 5, "2026-11-26", "2027-01-01")]
    [InlineData(25, 5, "2026-12-26", "2027-02-01")]
    // Vencimento no mesmo dia do fechamento: conta como mes seguinte.
    [InlineData(10, 10, "2026-11-10", "2026-12-01")]
    public void DueMonthOf_FollowsTheClosingDay(int closing, int due, string purchase, string expectedDueMonth) =>
        Assert.Equal(DateOnly.Parse(expectedDueMonth), new CardCycle(closing, due).DueMonthOf(DateOnly.Parse(purchase)));

    [Fact]
    public void PeriodOf_SameMonthDue_HasTheCycleDates()
    {
        var period = new CardCycle(10, 17).PeriodOf(D(2026, 11, 20));

        Assert.Equal(new InvoicePeriod(D(2026, 11, 1), D(2026, 10, 11), D(2026, 11, 10), D(2026, 11, 17)), period);
    }

    [Fact]
    public void PeriodOf_DueInTheFollowingMonth_ClosesTheMonthBefore_AcrossTheYear()
    {
        var period = new CardCycle(25, 5).PeriodOf(D(2027, 1, 1));

        Assert.Equal(new InvoicePeriod(D(2027, 1, 1), D(2026, 11, 26), D(2026, 12, 25), D(2027, 1, 5)), period);
    }

    [Fact]
    public void EveryDayOfAYear_FallsInsideThePeriodOfItsInvoice()
    {
        foreach (var cycle in new[] { new CardCycle(10, 17), new CardCycle(25, 5), new CardCycle(1, 28), new CardCycle(28, 1) })
        {
            for (var day = D(2026, 1, 1); day < D(2027, 1, 1); day = day.AddDays(1))
            {
                var period = cycle.PeriodOf(cycle.DueMonthOf(day));
                Assert.InRange(day, period.StartDate, period.ClosingDate);
                Assert.True(period.DueDate > period.ClosingDate);
            }
        }
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(29, 10)]
    [InlineData(10, 0)]
    [InlineData(10, 29)]
    public void Cycle_DaysOutsideOneTo28_AreRejected(int closing, int due) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CardCycle(closing, due));

    // ---------- Parcelas ----------

    [Theory]
    [InlineData("100.00", 3, new[] { "33.34", "33.33", "33.33" })]
    [InlineData("10.00", 4, new[] { "2.50", "2.50", "2.50", "2.50" })]
    [InlineData("0.05", 3, new[] { "0.03", "0.01", "0.01" })]
    [InlineData("199.90", 1, new[] { "199.90" })]
    public void Split_PutsTheRoundingDifferenceInTheFirst_AndAlwaysAddsUp(string amount, int count, string[] expected)
    {
        var total = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);

        var parts = Installments.Split(total, count);

        Assert.Equal(expected.Select(e => decimal.Parse(e, System.Globalization.CultureInfo.InvariantCulture)), parts);
        Assert.Equal(total, parts.Sum());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public void Split_OutsideOneTo24_IsRejected(int count) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Installments.Split(10m, count));

    // ---------- Faturas ----------

    [Fact]
    public void InstallmentPurchase_FallsInConsecutiveInvoicesStartingAtThePurchaseOne()
    {
        var purchase = Guid.NewGuid();

        var invoices = InvoiceCalculator.Calculate(new CardCycle(10, 17),
            [new CardCharge(purchase, "Eletronicos", 100m, D(2026, 11, 20), Installments: 3)], [], D(2026, 11, 21));

        Assert.Equal([D(2026, 12, 1), D(2027, 1, 1), D(2027, 2, 1)], invoices.Select(i => i.Period.DueMonth));
        Assert.Equal([33.34m, 33.33m, 33.33m], invoices.Select(i => i.Total));
        Assert.Equal([(1, 3), (2, 3), (3, 3)], invoices.Select(i => (i.Items[0].Installment, i.Items[0].Installments)));
        Assert.All(invoices, i => Assert.Equal((purchase, D(2026, 11, 20)), (i.Items[0].TransactionId, i.Items[0].PurchaseDate)));
    }

    [Fact]
    public void Refund_ReducesTheTotalOfTheInvoiceItFallsIn()
    {
        var invoice = Assert.Single(InvoiceCalculator.Calculate(new CardCycle(10, 17),
        [
            new CardCharge(Guid.NewGuid(), "Roupas", 300m, D(2026, 11, 2)),
            new CardCharge(Guid.NewGuid(), "Roupas", 120m, D(2026, 11, 5), IsRefund: true),
        ], [], D(2026, 11, 6)));

        Assert.Equal(180m, invoice.Total);
        Assert.Equal([300m, -120m], invoice.Items.Select(i => i.Amount));
    }

    [Fact]
    public void Payments_CountForTheirDueMonth_AndRemainingNeverGoesNegative()
    {
        var invoices = InvoiceCalculator.Calculate(new CardCycle(10, 17),
            [new CardCharge(Guid.NewGuid(), "Mercado", 500m, D(2026, 10, 5)), new CardCharge(Guid.NewGuid(), "Mercado", 80m, D(2026, 10, 20))],
            [new InvoicePayment(D(2026, 10, 15), 200m), new InvoicePayment(D(2026, 10, 1), 400m), new InvoicePayment(D(2026, 11, 1), 50m)],
            D(2026, 11, 12));

        var (october, november) = (invoices[0], invoices[1]);
        Assert.Equal((500m, 600m, 0m, InvoiceStatus.Paid), (october.Total, october.Paid, october.Remaining, october.Status));
        Assert.Equal((80m, 50m, 30m, InvoiceStatus.PartiallyPaid), (november.Total, november.Paid, november.Remaining, november.Status));
    }

    [Fact]
    public void PaymentWithoutPurchases_StillShowsTheInvoice()
    {
        var invoice = Assert.Single(InvoiceCalculator.Calculate(new CardCycle(10, 17), [],
            [new InvoicePayment(D(2026, 11, 1), 50m)], D(2026, 11, 21)));

        Assert.Equal((0m, 50m, 0m), (invoice.Total, invoice.Paid, invoice.Remaining));
        Assert.Empty(invoice.Items);
    }

    // Fatura que vence 2026-11-17 e fecha 2026-11-10.
    [Theory]
    [InlineData("2026-11-10", "0", InvoiceStatus.Open)]          // dia do fechamento: ainda aberta
    [InlineData("2026-11-05", "100", InvoiceStatus.Open)]        // pagamento antecipado nao fecha a fatura
    [InlineData("2026-11-11", "0", InvoiceStatus.Closed)]
    [InlineData("2026-11-17", "0", InvoiceStatus.Closed)]        // dia do vencimento: ainda no prazo
    [InlineData("2026-11-12", "40", InvoiceStatus.PartiallyPaid)]
    [InlineData("2026-11-12", "100", InvoiceStatus.Paid)]
    [InlineData("2026-11-12", "150", InvoiceStatus.Paid)]
    [InlineData("2026-11-18", "0", InvoiceStatus.Overdue)]
    [InlineData("2026-11-18", "40", InvoiceStatus.Overdue)]      // parcial depois do vencimento
    [InlineData("2026-11-18", "100", InvoiceStatus.Paid)]
    public void Status_FollowsClosingDueDateAndPayments(string today, string paid, InvoiceStatus expected)
    {
        var period = new CardCycle(10, 17).PeriodOf(D(2026, 11, 1));

        Assert.Equal(expected, InvoiceCalculator.StatusOf(period, 100m, decimal.Parse(paid), DateOnly.Parse(today)));
    }

    [Fact]
    public void ClosedInvoiceWithOnlyRefunds_HasNothingToPay()
    {
        var invoice = Assert.Single(InvoiceCalculator.Calculate(new CardCycle(10, 17),
            [new CardCharge(Guid.NewGuid(), "Roupas", 50m, D(2026, 11, 3), IsRefund: true)], [], D(2026, 11, 12)));

        Assert.Equal((-50m, 0m, InvoiceStatus.Paid), (invoice.Total, invoice.Remaining, invoice.Status));
    }
}
