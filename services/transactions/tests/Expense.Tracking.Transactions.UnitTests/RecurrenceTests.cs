using Expense.Tracking.Transactions.Domain.Recurrences;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.UnitTests;

/// <summary>REC-2 (ADR-0008, decisoes 3 a 6): a regra de recorrencia e a agenda dela.</summary>
public sealed class RecurrenceTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-23T12:00:00Z");

    private static Recurrence Monthly(DateOnly startsOn, DateOnly? endsOn = null) => Recurrence.Create(
        Owner, AccountId, TransactionType.Expense, PaymentMethod.Boleto, null, " Aluguel ", "Moradia", 2000m, null, null,
        RecurrenceFrequency.Monthly, startsOn, endsOn, Now).Value;

    [Fact]
    public void Create_NormalizesLikeATransaction_AndStartsAtVersionOne()
    {
        var rule = Monthly(new DateOnly(2026, 12, 10));

        Assert.Equal(("Aluguel", "BRL", 1, RecurrenceFrequency.Monthly), (rule.Description, rule.Currency, rule.Version, rule.Frequency));
    }

    [Fact]
    public void Create_UsesTheTransactionRules_AndRefusesAnEndBeforeTheStart()
    {
        var noDescription = Recurrence.Create(Owner, AccountId, TransactionType.Expense, PaymentMethod.Pix, null, " ", "Casa",
            10m, null, null, RecurrenceFrequency.Monthly, new DateOnly(2026, 12, 1), null, Now);
        var cardWithoutCard = Recurrence.Create(Owner, AccountId, TransactionType.Expense, PaymentMethod.CreditCard, null, "Streaming",
            "Lazer", 40m, null, null, RecurrenceFrequency.Monthly, new DateOnly(2026, 12, 1), null, Now);
        var backwards = Recurrence.Create(Owner, AccountId, TransactionType.Expense, PaymentMethod.Pix, null, "Escola", "Educacao",
            900m, null, null, RecurrenceFrequency.Monthly, new DateOnly(2026, 12, 1), new DateOnly(2026, 11, 30), Now);

        Assert.Equal("transaction.description_required", noDescription.Error.Code);
        Assert.StartsWith("transaction.", cardWithoutCard.Error.Code);
        Assert.Equal("recurrence.ends_before_start", backwards.Error.Code);
    }

    [Fact]
    public void Monthly_RepeatsOnTheStartDay_AndUsesTheLastDayOfShorterMonths()
    {
        var rule = Monthly(new DateOnly(2027, 1, 31));

        var dates = rule.Occurrences(new DateOnly(2027, 1, 1), new DateOnly(2027, 4, 30)).ToList();

        Assert.Equal(
            [("2027-01", new DateOnly(2027, 1, 31)), ("2027-02", new DateOnly(2027, 2, 28)),
             ("2027-03", new DateOnly(2027, 3, 31)), ("2027-04", new DateOnly(2027, 4, 30))],
            dates.Select(o => (o.Period, o.ScheduledOn)));
    }

    [Fact]
    public void Occurrences_StayBetweenStartEndAndTheAskedWindow()
    {
        var rule = Monthly(new DateOnly(2026, 11, 10), endsOn: new DateOnly(2027, 2, 9));

        var dates = rule.Occurrences(new DateOnly(2026, 11, 15), new DateOnly(2027, 12, 31)).Select(o => o.Period).ToList();

        // Novembro ja passou na janela (10 < 15); fevereiro cai em 10/02, depois do fim (09/02).
        Assert.Equal(["2026-12", "2027-01"], dates);
    }

    [Fact]
    public void Yearly_RepeatsOnTheStartDayAndMonth_AndFeb29FallsOn28()
    {
        var rule = Recurrence.Create(Owner, AccountId, TransactionType.Expense, PaymentMethod.Boleto, null, "Seguro", "Seguros",
            1200m, null, null, RecurrenceFrequency.Yearly, new DateOnly(2028, 2, 29), null, Now).Value;

        var dates = rule.Occurrences(new DateOnly(2028, 1, 1), new DateOnly(2030, 12, 31)).ToList();

        Assert.Equal(
            [("2028", new DateOnly(2028, 2, 29)), ("2029", new DateOnly(2029, 2, 28)), ("2030", new DateOnly(2030, 2, 28))],
            dates.Select(o => (o.Period, o.ScheduledOn)));
    }

    [Theory]
    [InlineData("2026-12", "2026-12-10")]
    [InlineData("2026-10", null)] // antes do inicio
    [InlineData("2027-03", null)] // depois do fim
    [InlineData("2026-13", null)]
    [InlineData("2026", null)]
    [InlineData("lixo", null)]
    public void ScheduledOn_GivesTheDateOfAPeriod_OrNullOutsideTheSchedule(string period, string? expected)
    {
        var rule = Monthly(new DateOnly(2026, 11, 10), endsOn: new DateOnly(2027, 2, 28));

        Assert.Equal(expected is null ? null : DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            rule.ScheduledOn(period));
    }

    // ---------- RJT-2 (ADR-0010, decisao 1): valor com vigencia ----------

    [Fact]
    public void AmountFrom_ChangesOnlyThatPeriodOnwards()
    {
        var rule = Monthly(new DateOnly(2030, 1, 10));

        var set = rule.SetAmountFrom("2030-03", 2100m, new HashSet<string>(), Now);

        Assert.True(set.IsSuccess);
        Assert.Equal((2000m, 2000m, 2100m, 2100m), (rule.AmountFor("2030-01"), rule.AmountFor("2030-02"),
            rule.AmountFor("2030-03"), rule.AmountFor("2031-01")));
        Assert.Equal([("2030-01", 2000m), ("2030-03", 2100m)], rule.Amounts.Select(a => (a.FromPeriod, a.Amount)));
        Assert.Equal(2, rule.Version);

        // Mesmo periodo de novo: troca o valor da vigencia.
        rule.SetAmountFrom("2030-03", 2150m, new HashSet<string>(), Now);
        Assert.Equal(2150m, rule.AmountFor("2030-04"));
        Assert.Equal(2, rule.LaterAmounts.Count + 1);
    }

    [Theory]
    [InlineData("2030-01")] // o primeiro periodo e a vigencia inicial
    [InlineData("2029-12")] // antes do inicio
    [InlineData("2030-02")] // periodo ja fechado
    [InlineData("marco")]
    public void AmountFrom_RefusesFirstEarlierClosedOrInvalidPeriods(string period)
    {
        var rule = Monthly(new DateOnly(2030, 1, 10));

        var set = rule.SetAmountFrom(period, 2100m, new HashSet<string> { "2030-02" }, Now);

        Assert.Equal("recurrence.amount_period_invalid", set.Error.Code);
        Assert.Equal(1, rule.Version);
    }

    [Fact]
    public void Edit_CorrectsTheCurrentAmount_AndRemovingOnlyAFutureAmountIsAllowed()
    {
        var rule = Monthly(new DateOnly(2030, 1, 10));
        rule.SetAmountFrom("2030-03", 2100m, new HashSet<string>(), Now);
        rule.SetAmountFrom("2030-06", 2200m, new HashSet<string>(), Now);

        // Em abril vale a vigencia de marco: o PUT corrige ela, nao a primeira.
        rule.Edit(TransactionType.Expense, PaymentMethod.Boleto, null, "Aluguel", "Moradia", 2120m, "BRL", null,
            RecurrenceFrequency.Monthly, new DateOnly(2030, 1, 10), null, Now, currentPeriod: "2030-04");
        Assert.Equal((2000m, 2120m, 2200m), (rule.AmountFor("2030-01"), rule.AmountFor("2030-04"), rule.AmountFor("2030-06")));

        Assert.Equal("recurrence.amount_already_started", rule.RemoveAmountFrom("2030-03", "2030-04", Now).Error.Code);
        Assert.Equal("recurrence.amount_not_found", rule.RemoveAmountFrom("2030-05", "2030-04", Now).Error.Code);
        Assert.True(rule.RemoveAmountFrom("2030-06", "2030-04", Now).IsSuccess);
        Assert.Equal(2120m, rule.AmountFor("2030-07"));

        // Mudar o inicio para depois de uma vigencia a deixa fora da agenda: recusado.
        var moved = rule.Edit(TransactionType.Expense, PaymentMethod.Boleto, null, "Aluguel", "Moradia", 2120m, "BRL", null,
            RecurrenceFrequency.Monthly, new DateOnly(2030, 4, 10), null, Now, currentPeriod: "2030-04");
        Assert.Equal("recurrence.amounts_outside_schedule", moved.Error.Code);
    }

    [Fact]
    public void Edit_ChangesTheRule_BumpsTheVersion_AndWithoutChangesTouchesNothing()
    {
        var rule = Monthly(new DateOnly(2026, 12, 10));

        var same = rule.Edit(TransactionType.Expense, PaymentMethod.Boleto, null, "Aluguel", "Moradia", 2000m, "BRL", null,
            RecurrenceFrequency.Monthly, new DateOnly(2026, 12, 10), null, Now.AddHours(1));
        Assert.False(same.Value);
        Assert.Equal((1, Now), (rule.Version, rule.UpdatedAtUtc));

        var changed = rule.Edit(TransactionType.Expense, PaymentMethod.Boleto, null, "Aluguel", "Moradia", 2100m, "BRL", null,
            RecurrenceFrequency.Monthly, new DateOnly(2026, 12, 12), new DateOnly(2027, 12, 31), Now.AddHours(1));
        Assert.True(changed.Value);
        Assert.Equal((2100m, new DateOnly(2026, 12, 12), 2, Now.AddHours(1)), (rule.Amount, rule.StartsOn, rule.Version, rule.UpdatedAtUtc));
    }
}
