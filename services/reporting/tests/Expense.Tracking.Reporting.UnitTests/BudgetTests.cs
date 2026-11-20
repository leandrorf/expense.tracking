using Expense.Tracking.Reporting.Domain.Budgets;

namespace Expense.Tracking.Reporting.UnitTests;

public sealed class BudgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Alimentação", "alimentacao")]
    [InlineData("  alimentação  ", "alimentacao")]
    [InlineData("ALIMENTACAO", "alimentacao")]
    [InlineData("Plano   de\tSaúde", "plano de saude")]
    [InlineData("Café & Padaria", "cafe & padaria")]
    public void CategoryKey_IgnoresCaseAccentsAndExtraSpaces(string category, string expected)
    {
        Assert.Equal(expected, CategoryKey.Normalize(category));
    }

    [Fact]
    public void Create_KeepsTheTypedCategory_AndNormalizesTheRest()
    {
        var result = Budget.Create(Guid.NewGuid(), "  Alimentação ", BudgetScope.Personal, 1500m, " brl ", new DateOnly(2026, 12, 17), Now);

        Assert.True(result.IsSuccess);
        var budget = result.Value;
        Assert.Equal("Alimentação", budget.Category);
        Assert.Equal("alimentacao", budget.CategoryKey);
        Assert.Equal("BRL", budget.Currency);
        Assert.Equal(new DateOnly(2026, 12, 1), budget.StartMonth);
        Assert.Equal(Now, budget.CreatedAt);
    }

    [Fact]
    public void Create_WithoutCurrency_DefaultsToBrl()
    {
        var result = Budget.Create(Guid.NewGuid(), "Fornecedores", BudgetScope.Business, 5000m, null, new DateOnly(2026, 12, 1), Now);

        Assert.Equal("BRL", result.Value.Currency);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    public void Create_RejectsInvalidAmounts(double amount)
    {
        var result = Budget.Create(Guid.NewGuid(), "Alimentação", BudgetScope.Personal, (decimal)amount, null, new DateOnly(2026, 12, 1), Now);

        Assert.True(result.IsFailure);
        Assert.Equal("budget.amount_invalid", result.Error.Code);
    }

    [Fact]
    public void Create_RejectsUndefinedScope()
    {
        var result = Budget.Create(Guid.NewGuid(), "Alimentação", (BudgetScope)99, 10m, null, new DateOnly(2026, 12, 1), Now);

        Assert.Equal("budget.scope_invalid", result.Error.Code);
    }

    private static Budget Food(decimal amount = 1500m, int year = 2026, int month = 12) =>
        Budget.Create(Guid.NewGuid(), "Alimentação", BudgetScope.Personal, amount, null, new DateOnly(year, month, 1), Now).Value;

    private static DateOnly Month(int year, int month) => new(year, month, 1);

    [Fact]
    public void ChangeAmount_AppliesFromTheMonth_AndKeepsThePreviousMonths()
    {
        var budget = Food(1500m);

        var result = budget.ChangeAmount(1800m, Month(2027, 2));

        Assert.True(result.IsSuccess);
        Assert.Null(budget.LimitFor(Month(2026, 11)));
        Assert.Equal(1500m, budget.LimitFor(Month(2026, 12)));
        Assert.Equal(1500m, budget.LimitFor(new DateOnly(2027, 1, 31)));
        Assert.Equal(1800m, budget.LimitFor(Month(2027, 2)));
        Assert.Equal(1800m, budget.LimitFor(Month(2030, 7)));
        Assert.Equal(1800m, budget.LatestAmount);
    }

    [Fact]
    public void ChangeAmount_InTheSameMonth_ReplacesTheValue()
    {
        var budget = Food(1500m);

        budget.ChangeAmount(1700m, Month(2026, 12));

        Assert.Equal([(Month(2026, 12), 1700m)], budget.Limits.Select(l => (l.EffectiveFrom, l.Amount)).ToArray());
    }

    [Fact]
    public void ChangeAmount_ReplacesTheLaterVersions()
    {
        var budget = Food(1500m);
        budget.ChangeAmount(2000m, Month(2027, 5));

        budget.ChangeAmount(1800m, Month(2027, 2));

        Assert.Equal(
            [(Month(2026, 12), 1500m), (Month(2027, 2), 1800m)],
            budget.Limits.Select(l => (l.EffectiveFrom, l.Amount)).ToArray());
    }

    [Fact]
    public void ChangeAmount_BeforeTheStart_IsRejected()
    {
        var result = Food().ChangeAmount(1800m, Month(2026, 11));

        Assert.Equal("budget.month_before_start", result.Error.Code);
    }

    [Fact]
    public void ChangeAmount_WithInvalidValue_IsRejected()
    {
        var result = Food().ChangeAmount(0m, Month(2027, 1));

        Assert.Equal("budget.amount_invalid", result.Error.Code);
    }

    [Fact]
    public void Close_StopsTheBudgetFromThatMonth_AndKeepsThePreviousMonths()
    {
        var budget = Food(1500m);
        budget.ChangeAmount(1800m, Month(2027, 2));

        var result = budget.Close(Month(2027, 4));

        Assert.True(result.IsSuccess);
        Assert.Equal(1500m, budget.LimitFor(Month(2026, 12)));
        Assert.Equal(1800m, budget.LimitFor(Month(2027, 3)));
        Assert.Null(budget.LimitFor(Month(2027, 4)));
        Assert.Null(budget.LimitFor(Month(2028, 1)));
        Assert.Equal(Month(2027, 4), budget.ClosedFromMonth);
        Assert.Null(budget.OpenCategoryKey);
    }

    [Fact]
    public void Close_DropsTheVersionsThatWouldNeverApply()
    {
        var budget = Food(1500m);
        budget.ChangeAmount(1800m, Month(2027, 6));

        budget.Close(Month(2027, 3));

        Assert.Equal([Month(2026, 12)], budget.Limits.Select(l => l.EffectiveFrom).ToArray());
    }

    [Fact]
    public void Close_InTheStartMonthOrBefore_IsRejected()
    {
        Assert.Equal("budget.month_before_start", Food().Close(Month(2026, 12)).Error.Code);
        Assert.Equal("budget.month_before_start", Food().Close(Month(2026, 1)).Error.Code);
    }

    [Fact]
    public void Close_Twice_IsAConflict()
    {
        var budget = Food();
        budget.Close(Month(2027, 3));

        Assert.Equal("budget.already_closed", budget.Close(Month(2027, 5)).Error.Code);
    }

    [Fact]
    public void ChangeAmount_AfterTheClosing_IsAConflict()
    {
        var budget = Food();
        budget.Close(Month(2027, 3));

        Assert.Equal("budget.closed", budget.ChangeAmount(10m, Month(2027, 3)).Error.Code);
        Assert.True(budget.ChangeAmount(10m, Month(2027, 2)).IsSuccess);
    }

    [Theory]
    [InlineData(2027, 2, true)]
    [InlineData(2027, 3, false)]
    [InlineData(2028, 1, false)]
    [InlineData(2026, 1, true)]
    public void OverlapsFrom_ConsidersTheClosing(int year, int month, bool overlaps)
    {
        var budget = Food();
        budget.Close(Month(2027, 3));

        Assert.Equal(overlaps, budget.OverlapsFrom(Month(year, month)));
    }
}
