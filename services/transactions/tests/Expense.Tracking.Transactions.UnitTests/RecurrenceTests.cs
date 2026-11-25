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

    // ---------- RJT-4 (ADR-0010, decisoes 3 a 5): reajuste anual ----------

    private static readonly RecurrenceAdjustmentSpec FivePercentInMarch = new(3, RecurrenceAdjustmentKind.Fixed, null, 5m);

    private static Recurrence Adjusted(RecurrenceAdjustmentSpec? adjustment, decimal amount = 2000m, string frequency = "Monthly",
        DateOnly? startsOn = null) => Recurrence.Create(
        Owner, AccountId, TransactionType.Expense, PaymentMethod.Boleto, null, "Aluguel", "Moradia", amount, null, null,
        Enum.Parse<RecurrenceFrequency>(frequency), startsOn ?? new DateOnly(2030, 1, 10), null, Now, adjustment).Value;

    [Fact]
    public void Adjustment_IsKeptOnTheRule_WithTheStartMonthAsDefault()
    {
        Assert.Equal(new RecurrenceAdjustment(3, RecurrenceAdjustmentKind.Fixed, null, 5m, true), Adjusted(FivePercentInMarch).Adjustment);
        Assert.Equal(new RecurrenceAdjustment(1, RecurrenceAdjustmentKind.Index, EconomicIndex.IGPM, null, false),
            Adjusted(new RecurrenceAdjustmentSpec(null, RecurrenceAdjustmentKind.Index, EconomicIndex.IGPM, 7m, NoReduction: false)).Adjustment);
        Assert.Null(Adjusted(null).Adjustment);
    }

    [Theory]
    [InlineData(13, RecurrenceAdjustmentKind.Fixed, null, 5.0, "Monthly", "recurrence.adjustment_month_invalid")]
    [InlineData(3, RecurrenceAdjustmentKind.Fixed, null, 5.0, "Yearly", "recurrence.adjustment_month_invalid")] // anual: o mes dela
    [InlineData(3, RecurrenceAdjustmentKind.Index, null, null, "Monthly", "recurrence.adjustment_index_invalid")]
    [InlineData(3, RecurrenceAdjustmentKind.Fixed, null, null, "Monthly", "recurrence.adjustment_rate_invalid")]
    [InlineData(3, RecurrenceAdjustmentKind.Fixed, null, -100.0, "Monthly", "recurrence.adjustment_rate_invalid")]
    [InlineData(3, RecurrenceAdjustmentKind.Fixed, null, 1.23456, "Monthly", "recurrence.adjustment_rate_invalid")]
    public void Adjustment_RefusesInvalidConfigurations(
        int month, RecurrenceAdjustmentKind kind, EconomicIndex? index, double? rate, string frequency, string code)
    {
        var created = Recurrence.Create(Owner, AccountId, TransactionType.Expense, PaymentMethod.Boleto, null, "Aluguel", "Moradia",
            2000m, null, null, Enum.Parse<RecurrenceFrequency>(frequency), new DateOnly(2030, 1, 10), null, Now,
            new RecurrenceAdjustmentSpec(month, kind, index, (decimal?)rate));

        Assert.Equal(code, created.Error.Code);
    }

    [Fact]
    public void ApplyAdjustment_CreatesTheAmountFromTheAnniversary_OnlyOncePerYear()
    {
        var rule = Adjusted(FivePercentInMarch);
        var none = new HashSet<string>();

        Assert.True(rule.ApplyAdjustment(2030, 4.5m, null, none, Now).IsSuccess);
        Assert.Equal((2000m, 2090m, 2), (rule.AmountFor("2030-02"), rule.AmountFor("2030-03"), rule.Version));
        Assert.Equal("recurrence.adjustment_already_applied", rule.ApplyAdjustment(2030, 5m, null, none, Now).Error.Code);

        // O ano seguinte parte do valor vigente antes dele; o valor novo informado vale como esta.
        Assert.True(rule.ApplyAdjustment(2031, null, 2150m, none, Now).IsSuccess);
        Assert.Equal(2150m, rule.AmountFor("2031-03"));
        Assert.Equal([(2030, 2000m, 2090m), (2031, 2090m, 2150m)],
            rule.AppliedAdjustments.Select(a => (a.Year, a.PreviousAmount, a.NewAmount)));
    }

    [Fact]
    public void ApplyAdjustment_RefusesYearsOutsideTheRule_MissingValues_ClosedPeriods_AndUnconfiguredRules()
    {
        var rule = Adjusted(FivePercentInMarch);
        var none = new HashSet<string>();

        Assert.Equal("recurrence.adjustment_year_invalid", rule.ApplyAdjustment(2029, 5m, null, none, Now).Error.Code);
        Assert.Equal("recurrence.adjustment_value_required", rule.ApplyAdjustment(2030, null, null, none, Now).Error.Code);
        Assert.Equal("recurrence.adjustment_value_required", rule.ApplyAdjustment(2030, 5m, 2100m, none, Now).Error.Code);
        Assert.Equal("recurrence.amount_period_invalid", rule.ApplyAdjustment(2030, 5m, null, new HashSet<string> { "2030-03" }, Now).Error.Code);
        Assert.Equal("recurrence.adjustment_not_configured", Adjusted(null).ApplyAdjustment(2030, 5m, null, none, Now).Error.Code);
        Assert.Equal(1, rule.Version);

        // No aniversario do inicio nao ha reajuste (o contrato acabou de comecar).
        var marchStart = Adjusted(FivePercentInMarch, startsOn: new DateOnly(2030, 3, 10));
        Assert.Equal("recurrence.adjustment_year_invalid", marchStart.ApplyAdjustment(2030, 5m, null, none, Now).Error.Code);
    }

    [Fact]
    public void ApplyAdjustment_RoundsHalfUp_AndNoReductionKeepsThePreviousAmount()
    {
        var rounded = Adjusted(FivePercentInMarch, amount: 1000.05m);
        rounded.ApplyAdjustment(2030, 10m, null, new HashSet<string>(), Now);
        Assert.Equal(1100.06m, rounded.AmountFor("2030-03")); // 1100,055

        var kept = Adjusted(FivePercentInMarch);
        kept.ApplyAdjustment(2030, -2m, null, new HashSet<string>(), Now);
        Assert.Equal(2000m, kept.AmountFor("2030-03"));

        var reduced = Adjusted(FivePercentInMarch with { NoReduction = false });
        reduced.ApplyAdjustment(2030, -2m, null, new HashSet<string>(), Now);
        Assert.Equal(1960m, reduced.AmountFor("2030-03"));
    }

    [Fact]
    public void PendingAdjustment_ShowsTheLatestAnniversary_UntilItIsApplied()
    {
        var rule = Adjusted(FivePercentInMarch);

        Assert.Null(rule.PendingAdjustment(new DateOnly(2030, 2, 20))); // aniversario anterior (2029-03) fora da regra
        Assert.Equal(new RecurrencePendingAdjustment(2030, "2030-03", 2000m, 5m, 2100m), rule.PendingAdjustment(new DateOnly(2030, 3, 1)));
        Assert.Equal(2030, rule.PendingAdjustment(new DateOnly(2031, 2, 28))!.Year); // ate 11 meses depois

        rule.ApplyAdjustment(2030, 5m, null, new HashSet<string>(), Now);
        Assert.Null(rule.PendingAdjustment(new DateOnly(2030, 3, 1)));
        Assert.Equal(new RecurrencePendingAdjustment(2031, "2031-03", 2100m, 5m, 2205m), rule.PendingAdjustment(new DateOnly(2031, 3, 1)));

        // Pelo indice, a sugestao fica para quando houver indices (RJT-7).
        var byIndex = Adjusted(new RecurrenceAdjustmentSpec(3, RecurrenceAdjustmentKind.Index, EconomicIndex.IPCA, null));
        Assert.Equal(new RecurrencePendingAdjustment(2030, "2030-03", 2000m, null, null), byIndex.PendingAdjustment(new DateOnly(2030, 3, 1)));

        // Anual: o periodo e o ano.
        var yearly = Adjusted(new RecurrenceAdjustmentSpec(null, RecurrenceAdjustmentKind.Fixed, null, 10m), amount: 1200m, frequency: "Yearly");
        Assert.Equal(new RecurrencePendingAdjustment(2031, "2031", 1200m, 10m, 1320m), yearly.PendingAdjustment(new DateOnly(2031, 1, 10)));
    }

    [Fact]
    public void RemovingTheAdjustedAmount_AllowsApplyingAgain_AndEditingTheAdjustmentBumpsTheVersion()
    {
        var rule = Adjusted(FivePercentInMarch);
        rule.ApplyAdjustment(2030, 5m, null, new HashSet<string>(), Now);
        rule.RemoveAmountFrom("2030-03", "2030-01", Now);
        Assert.True(rule.ApplyAdjustment(2030, 6m, null, new HashSet<string>(), Now).IsSuccess);
        Assert.Equal(2120m, rule.AmountFor("2030-03"));

        var version = rule.Version;
        var same = rule.Edit(TransactionType.Expense, PaymentMethod.Boleto, null, "Aluguel", "Moradia", 2000m, "BRL", null,
            RecurrenceFrequency.Monthly, new DateOnly(2030, 1, 10), null, Now, "2030-01", FivePercentInMarch);
        Assert.False(same.Value);

        var removed = rule.Edit(TransactionType.Expense, PaymentMethod.Boleto, null, "Aluguel", "Moradia", 2000m, "BRL", null,
            RecurrenceFrequency.Monthly, new DateOnly(2030, 1, 10), null, Now, "2030-01", adjustment: null);
        Assert.True(removed.Value);
        Assert.Equal((version + 1, (RecurrenceAdjustment?)null), (rule.Version, rule.Adjustment));
    }
}
