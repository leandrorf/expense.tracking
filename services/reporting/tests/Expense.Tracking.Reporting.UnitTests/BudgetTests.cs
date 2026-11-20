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
}
