using Expense.Tracking.Reporting.Domain.Budgets;
using Expense.Tracking.Reporting.Domain.Reports;

namespace Expense.Tracking.Reporting.UnitTests;

public sealed class BudgetStatusCalculatorTests
{
    private static readonly DateOnly December = new(2026, 12, 1);
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static Budget NewBudget(
        string category, BudgetScope scope, decimal amount, string currency = "BRL", DateOnly? start = null, int? warningThreshold = null) =>
        Budget.Create(Guid.NewGuid(), category, scope, amount, currency, start ?? December, Now, warningThreshold).Value;

    private static TransactionRecord Expense(
        string category, decimal amount, string? scope = "Personal", string method = "Pix", string currency = "BRL",
        string type = "Expense") =>
        new(Guid.NewGuid(), type, method, category, amount, currency, scope);

    [Fact]
    public void Pf_SumsAllPaymentMethodsAndAccounts_AndIsOnTrackBelow80Percent()
    {
        var budget = NewBudget("Alimentação", BudgetScope.Personal, 1000m);

        var status = Assert.Single(BudgetStatusCalculator.Calculate(December, [budget],
        [
            Expense("Alimentação", 300m, method: "Pix"),
            Expense("Alimentação", 200m, method: "Cash"),
            Expense("Alimentação", 250m, method: "CreditCard"),
        ]));

        Assert.Equal(1000m, status.Limit);
        Assert.Equal(750m, status.Spent);
        Assert.Equal(250m, status.Remaining);
        Assert.Equal(75m, status.Percent);
        Assert.Equal(BudgetHealth.OnTrack, status.Health);
    }

    [Fact]
    public void Pj_From80Percent_IsWarning()
    {
        var budget = NewBudget("Fornecedores", BudgetScope.Business, 5000m);

        var status = Assert.Single(BudgetStatusCalculator.Calculate(December, [budget],
        [
            Expense("Fornecedores", 3000m, scope: "Business", method: "Boleto"),
            Expense("Fornecedores", 1200m, scope: "Business", method: "BankTransfer"),
        ]));

        Assert.Equal(84m, status.Percent);
        Assert.Equal(BudgetHealth.Warning, status.Health);
    }

    [Theory]
    [InlineData(799.99, BudgetHealth.OnTrack)]
    [InlineData(800, BudgetHealth.Warning)]
    [InlineData(1000, BudgetHealth.Warning)]
    [InlineData(1000.01, BudgetHealth.Exceeded)]
    public void Health_Boundaries(double spent, BudgetHealth expected)
    {
        var budget = NewBudget("Alimentação", BudgetScope.Personal, 1000m);

        var status = Assert.Single(BudgetStatusCalculator.Calculate(December, [budget], [Expense("Alimentação", (decimal)spent)]));

        Assert.Equal(expected, status.Health);
    }

    [Fact]
    public void OverTheLimit_IsExceeded_WithNegativeRemaining()
    {
        var budget = NewBudget("Alimentação", BudgetScope.Personal, 1000m);

        var status = Assert.Single(BudgetStatusCalculator.Calculate(December, [budget], [Expense("Alimentação", 1250m)]));

        Assert.Equal(-250m, status.Remaining);
        Assert.Equal(125m, status.Percent);
        Assert.Equal(BudgetHealth.Exceeded, status.Health);
    }

    [Fact]
    public void ScopesNeverMix()
    {
        var pf = NewBudget("Alimentação", BudgetScope.Personal, 1000m);
        var pj = NewBudget("Alimentação", BudgetScope.Business, 1000m);

        var statuses = BudgetStatusCalculator.Calculate(December, [pf, pj],
        [
            Expense("Alimentação", 100m, scope: "Personal"),
            Expense("Alimentação", 400m, scope: "Business", method: "CreditCard"),
        ]);

        Assert.Equal(
            [(BudgetScope.Personal, 100m), (BudgetScope.Business, 400m)],
            statuses.Select(s => (s.Scope, s.Spent)).ToArray());
    }

    [Fact]
    public void IncomeOtherCurrencyAndUnknownScope_AreNotCounted()
    {
        var budget = NewBudget("Alimentação", BudgetScope.Personal, 1000m);

        var status = Assert.Single(BudgetStatusCalculator.Calculate(December, [budget],
        [
            Expense("Alimentação", 100m),
            Expense("Alimentação", 500m, type: "Income"),
            Expense("Alimentação", 500m, currency: "USD"),
            Expense("Alimentação", 500m, scope: null),
        ]));

        Assert.Equal(100m, status.Spent);
    }

    [Theory]
    [InlineData("alimentação ")]
    [InlineData("ALIMENTACAO")]
    [InlineData("  Alimentacao")]
    public void CategoryVariants_AreCounted(string typed)
    {
        var budget = NewBudget("Alimentação", BudgetScope.Personal, 1000m);

        var status = Assert.Single(BudgetStatusCalculator.Calculate(December, [budget], [Expense(typed, 100m)]));

        Assert.Equal(100m, status.Spent);
    }

    [Fact]
    public void BudgetWithoutSpending_IsListedWithZero()
    {
        var budget = NewBudget("Lazer", BudgetScope.Personal, 300m);

        var status = Assert.Single(BudgetStatusCalculator.Calculate(December, [budget], [Expense("Alimentação", 100m)]));

        Assert.Equal(0m, status.Spent);
        Assert.Equal(300m, status.Remaining);
        Assert.Equal(BudgetHealth.OnTrack, status.Health);
    }

    [Fact]
    public void UsesTheLimitOfTheMonth_AndSkipsBudgetsNotValidInIt()
    {
        var changed = NewBudget("Alimentação", BudgetScope.Personal, 1000m);
        changed.ChangeAmount(2000m, new DateOnly(2027, 1, 1));
        var future = NewBudget("Lazer", BudgetScope.Personal, 300m, start: new DateOnly(2027, 2, 1));
        var closed = NewBudget("Viagens", BudgetScope.Personal, 300m, start: new DateOnly(2026, 10, 1));
        closed.Close(December);

        var january = BudgetStatusCalculator.Calculate(new DateOnly(2027, 1, 1), [changed, future, closed], []);
        var december = BudgetStatusCalculator.Calculate(December, [changed, future, closed], []);

        Assert.Equal(2000m, Assert.Single(january).Limit);
        Assert.Equal(1000m, Assert.Single(december).Limit);
    }

    [Fact]
    public void BudgetInAnotherCurrency_CountsOnlyThatCurrency()
    {
        var budget = NewBudget("Software", BudgetScope.Business, 100m, currency: "USD");

        var status = Assert.Single(BudgetStatusCalculator.Calculate(December, [budget],
        [
            Expense("Software", 40m, scope: "Business", currency: "USD"),
            Expense("Software", 900m, scope: "Business", currency: "BRL"),
        ]));

        Assert.Equal(40m, status.Spent);
    }

    [Fact]
    public void Unbudgeted_PjTravelWithoutPjBudget_IsListedByScopeWithItsCategories()
    {
        var pjSuppliers = NewBudget("Fornecedores", BudgetScope.Business, 5000m);
        var pfTravel = NewBudget("Viagens", BudgetScope.Personal, 1000m);

        var unbudgeted = Assert.Single(BudgetStatusCalculator.CalculateUnbudgeted(December, [pjSuppliers, pfTravel],
        [
            Expense("Viagens", 800m, scope: "Business", method: "CreditCard"),
            Expense("viagens ", 200m, scope: "Business", method: "Pix"),
            Expense("Hospedagem", 1500m, scope: "Business"),
            Expense("Fornecedores", 3000m, scope: "Business", method: "Boleto"),
            Expense("Viagens", 400m, scope: "Personal"),
        ]));

        Assert.Equal(BudgetScope.Business, unbudgeted.Scope);
        Assert.Equal("BRL", unbudgeted.Currency);
        Assert.Equal(2500m, unbudgeted.Total);
        Assert.Equal(
            [new CategorySpending("Hospedagem", 1500m), new CategorySpending("Viagens", 1000m)],
            unbudgeted.Categories);
    }

    [Fact]
    public void Unbudgeted_IgnoresIncomeAndUnknownScope_AndSplitsByScopeAndCurrency()
    {
        var result = BudgetStatusCalculator.CalculateUnbudgeted(December, [],
        [
            Expense("Lazer", 100m),
            Expense("Salário", 9000m, type: "Income"),
            Expense("Lazer", 70m, scope: null),
            Expense("Lazer", 50m, scope: "Unknown"),
            Expense("Software", 30m, scope: "Business", currency: "USD"),
            Expense("Software", 60m, scope: "Business"),
        ]);

        Assert.Equal(
            [(BudgetScope.Personal, "BRL", 100m), (BudgetScope.Business, "BRL", 60m), (BudgetScope.Business, "USD", 30m)],
            result.Select(u => (u.Scope, u.Currency, u.Total)).ToArray());
    }

    [Fact]
    public void Unbudgeted_CountsBudgetsNotValidInTheMonth_AsUnbudgeted()
    {
        var closed = NewBudget("Viagens", BudgetScope.Personal, 300m, start: new DateOnly(2026, 10, 1));
        closed.Close(December);
        var future = NewBudget("Lazer", BudgetScope.Personal, 300m, start: new DateOnly(2027, 1, 1));

        var unbudgeted = Assert.Single(BudgetStatusCalculator.CalculateUnbudgeted(December, [closed, future],
            [Expense("Viagens", 100m), Expense("Lazer", 50m)]));

        Assert.Equal(150m, unbudgeted.Total);
    }

    [Fact]
    public void Unbudgeted_CategoryVariants_ShowTheMostUsedSpelling()
    {
        var unbudgeted = Assert.Single(BudgetStatusCalculator.CalculateUnbudgeted(December, [],
        [
            Expense("alimentacao", 10m),
            Expense("Alimentação", 20m),
            Expense(" Alimentação ", 30m),
        ]));

        Assert.Equal(new CategorySpending("Alimentação", 60m), Assert.Single(unbudgeted.Categories));
    }

    [Theory]
    [InlineData(850, BudgetHealth.OnTrack)]
    [InlineData(899.99, BudgetHealth.OnTrack)]
    [InlineData(900, BudgetHealth.Warning)]
    [InlineData(1000, BudgetHealth.Warning)]
    [InlineData(1000.01, BudgetHealth.Exceeded)]
    public void CustomWarningThreshold_90Percent(double spent, BudgetHealth expected)
    {
        var budget = NewBudget("Alimentação", BudgetScope.Personal, 1000m, warningThreshold: 90);

        var status = Assert.Single(BudgetStatusCalculator.Calculate(December, [budget], [Expense("Alimentação", (decimal)spent)]));

        Assert.Equal(expected, status.Health);
        Assert.Equal(90, status.WarningThreshold);
    }

    [Fact]
    public void WarningThreshold_100Percent_WarnsOnlyAtExactlyTheLimit()
    {
        var budget = NewBudget("Alimentação", BudgetScope.Personal, 1000m, warningThreshold: 100);

        Assert.Equal(BudgetHealth.OnTrack,
            Assert.Single(BudgetStatusCalculator.Calculate(December, [budget], [Expense("Alimentação", 999.99m)])).Health);
        Assert.Equal(BudgetHealth.Warning,
            Assert.Single(BudgetStatusCalculator.Calculate(December, [budget], [Expense("Alimentação", 1000m)])).Health);
    }
}
