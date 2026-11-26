using Expense.Tracking.Accounts.Domain.Cards;

namespace Expense.Tracking.Accounts.UnitTests;

/// <summary>
/// CYC (ADR-0012, decisoes 2 a 4): periodos encadeados com ciclos de vigencia e a troca de ciclo no cartao. Os
/// exemplos de <see cref="CardCycleSchedule"/> sao os mesmos do reporting (decisao 6).
/// </summary>
public sealed class CardCycleTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-26T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private static readonly DateOnly Today = new(2026, 11, 26);

    private static DateOnly D(string iso) => DateOnly.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    private static CardCycleSchedule Schedule(params CardCycleTerm[] terms) => new(terms);

    // ---------- Exemplos da regra (iguais no reporting) ----------

    [Fact]
    public void SingleCycle_IsTheMonthlyCycle()
    {
        var s = Schedule(new CardCycleTerm(null, 10, 17));

        Assert.Equal(new CardInvoicePeriod(D("2026-12-01"), D("2026-11-11"), D("2026-12-10"), D("2026-12-17")), s.PeriodOf(D("2026-12-15")));
        Assert.Equal((D("2026-11-01"), D("2026-12-01")), (s.DueMonthOf(D("2026-11-10")), s.DueMonthOf(D("2026-11-11"))));
    }

    [Fact]
    public void ChangeToALaterClosing_MakesAShortTransitionInvoice()
    {
        // Fecha 10 e vence 17; a partir da fatura de mar/2027, fecha 25 e vence 5 do mes seguinte.
        var s = Schedule(new CardCycleTerm(null, 10, 17), new CardCycleTerm(D("2027-03-01"), 25, 5));

        Assert.Equal(new CardInvoicePeriod(D("2027-02-01"), D("2027-01-11"), D("2027-02-10"), D("2027-02-17")), s.PeriodOf(D("2027-02-01")));
        Assert.Equal(new CardInvoicePeriod(D("2027-03-01"), D("2027-02-11"), D("2027-02-25"), D("2027-03-05")), s.PeriodOf(D("2027-03-01")));
        Assert.Equal(new CardInvoicePeriod(D("2027-04-01"), D("2027-02-26"), D("2027-03-25"), D("2027-04-05")), s.PeriodOf(D("2027-04-01")));
        Assert.Equal([D("2027-02-01"), D("2027-03-01"), D("2027-03-01"), D("2027-04-01")],
            new[] { D("2027-02-10"), D("2027-02-11"), D("2027-02-25"), D("2027-02-26") }.Select(s.DueMonthOf));
    }

    [Fact]
    public void ChangeToAnEarlierClosing_MakesALongTransitionInvoice()
    {
        // Fecha 25 e vence 5 do mes seguinte; a partir de mar/2027, fecha 5 e vence 15 no mesmo mes.
        var s = Schedule(new CardCycleTerm(null, 25, 5), new CardCycleTerm(D("2027-03-01"), 5, 15));

        Assert.Equal(new CardInvoicePeriod(D("2027-03-01"), D("2027-01-26"), D("2027-03-05"), D("2027-03-15")), s.PeriodOf(D("2027-03-01")));
        Assert.Equal(D("2027-03-01"), s.DueMonthOf(D("2027-02-20")));
    }

    [Fact]
    public void AnOldInvoiceClosingAfterTheFirstNewClosing_DoesNotExist_AndTheDueMonthSkips()
    {
        // Fecha 5 e vence 15; a partir de abr/2027, fecha 3 e vence 1 do mes seguinte: a de marco (fecharia 5/3) some.
        var s = Schedule(new CardCycleTerm(null, 5, 15), new CardCycleTerm(D("2027-04-01"), 3, 1));

        Assert.True(s.Exists(D("2027-02-01")));
        Assert.False(s.Exists(D("2027-03-01")));
        Assert.Equal(new CardInvoicePeriod(D("2027-04-01"), D("2027-02-06"), D("2027-03-03"), D("2027-04-01")), s.PeriodOf(D("2027-04-01")));
        Assert.Equal([D("2027-02-01"), D("2027-04-01"), D("2027-04-01"), D("2027-05-01")],
            new[] { D("2027-02-05"), D("2027-02-06"), D("2027-03-03"), D("2027-03-04") }.Select(s.DueMonthOf));
    }

    // ---------- Troca no cartao ----------

    private static CreditCard Card(int closingDay = 10, int dueDay = 17) =>
        CreditCard.Create(Guid.NewGuid(), Guid.NewGuid(), "Nubank", "Visa", "1234", 5000m, closingDay, dueDay, Now).Value;

    [Fact]
    public void ChangeCycle_FromAFutureInvoice_IsPending_AndTheCurrentCycleStays()
    {
        var card = Card();

        Assert.True(card.ChangeCycle(25, 5, D("2027-01-01"), Today, Now).IsSuccess);

        Assert.Equal([(null, 10, 17), (D("2027-01-01"), 25, 5)], card.Cycles.Terms.Select(t => (t.FromDueMonth, t.ClosingDay, t.DueDay)));
        Assert.Equal((10, 17), (card.CurrentCycle(Today).ClosingDay, card.CurrentCycle(Today).DueDay));
        // A fatura da troca vai de 11/12 a 25/12.
        Assert.Equal(new CardInvoicePeriod(D("2027-01-01"), D("2026-12-11"), D("2026-12-25"), D("2027-01-05")), card.Cycles.PeriodOf(D("2027-01-01")));
    }

    [Fact]
    public void ChangeCycle_RefusesClosedInvoices_AndSaysTheNextPossible()
    {
        var card = Card();

        // A de nov/2026 fechou em 10/11; a de dez/2026 com fechamento 25 fecharia em 25/11 (ja passou).
        var closed = card.ChangeCycle(25, 5, D("2026-11-01"), Today, Now);
        var newClosingPassed = card.ChangeCycle(25, 5, D("2026-12-01"), Today, Now);

        Assert.Equal("card.cycle_change_invalid", closed.Error.Code);
        Assert.Contains("01/2027", newClosingPassed.Error.Description);
        Assert.Single(card.Cycles.Terms);
    }

    [Fact]
    public void ChangeCycle_OnTheOpenInvoice_StartsNow_AndCannotBeUndone()
    {
        var card = Card();

        // A fatura aberta (dez/2026, de 11/11 a 10/12) passa a fechar em 5/12.
        Assert.True(card.ChangeCycle(5, 15, D("2026-12-01"), Today, Now).IsSuccess);

        Assert.Equal((5, 15), (card.CurrentCycle(Today).ClosingDay, card.CurrentCycle(Today).DueDay));
        Assert.Equal("card.cycle_already_started", card.RemoveCycleChange(D("2026-12-01"), Today, Now).Error.Code);
    }

    [Fact]
    public void ChangeCycle_ReplacesThePendingOne_WhichCanAlsoBeUndone()
    {
        var card = Card();
        card.ChangeCycle(25, 5, D("2027-01-01"), Today, Now);

        Assert.True(card.ChangeCycle(20, 28, D("2027-02-01"), Today, Now).IsSuccess);
        Assert.Equal([null, D("2027-02-01")], card.Cycles.Terms.Select(t => t.FromDueMonth));

        Assert.Equal("card.cycle_not_found", card.RemoveCycleChange(D("2027-01-01"), Today, Now).Error.Code);
        Assert.True(card.RemoveCycleChange(D("2027-02-01"), Today, Now).IsSuccess);
        Assert.Single(card.Cycles.Terms);
    }

    [Fact]
    public void ChangeCycle_RefusesInvalidDaysAndInactiveCards()
    {
        var card = Card();
        Assert.Equal("card.day_invalid", card.ChangeCycle(29, 5, D("2027-01-01"), Today, Now).Error.Code);

        card.Deactivate(Now);
        Assert.Equal("card.inactive", card.ChangeCycle(25, 5, D("2027-01-01"), Today, Now).Error.Code);
    }
}
