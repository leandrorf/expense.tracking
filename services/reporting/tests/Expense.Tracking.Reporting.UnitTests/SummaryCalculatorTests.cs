using Expense.Tracking.Reporting.Domain.Reports;

namespace Expense.Tracking.Reporting.UnitTests;

public sealed class SummaryCalculatorTests
{
    private static readonly Guid PfAccount = Guid.NewGuid();
    private static readonly Guid PjAccount = Guid.NewGuid();

    private static readonly IReadOnlyDictionary<Guid, string> Scopes = new Dictionary<Guid, string>
    {
        [PfAccount] = "Personal",
        [PjAccount] = "Business",
    };

    private static TransactionRecord Expense(Guid account, string category, string method, decimal amount) =>
        new(account, "Expense", method, category, amount, "BRL");

    [Fact]
    public void Calculate_EmptyList_ReturnsZeroedSummaryWithDefaultCurrency()
    {
        var summary = SummaryCalculator.Calculate([], Scopes);

        Assert.Equal("BRL", summary.Currency);
        Assert.Equal(0, summary.TotalExpense);
        Assert.Equal(0, summary.TotalIncome);
        Assert.Equal(0, summary.Net);
        Assert.Equal(0, summary.Count);
        Assert.Empty(summary.ByCategory);
    }

    [Fact]
    public void Calculate_ComputesTotalsAndNet()
    {
        var records = new[]
        {
            Expense(PfAccount, "Alimentacao", "Pix", 100m),
            Expense(PjAccount, "Software", "CreditCard", 300m),
            new TransactionRecord(PfAccount, "Income", "BankTransfer", "Salario", 500m, "BRL"),
        };

        var summary = SummaryCalculator.Calculate(records, Scopes);

        Assert.Equal(400m, summary.TotalExpense);
        Assert.Equal(500m, summary.TotalIncome);
        Assert.Equal(100m, summary.Net);
        Assert.Equal(3, summary.Count);
    }

    [Fact]
    public void Calculate_GroupsExpensesByCategory_SortedDesc()
    {
        var records = new[]
        {
            Expense(PfAccount, "Alimentacao", "Pix", 50m),
            Expense(PfAccount, "Alimentacao", "Cash", 70m),
            Expense(PfAccount, "Transporte", "Pix", 200m),
        };

        var summary = SummaryCalculator.Calculate(records, Scopes);

        Assert.Equal(2, summary.ByCategory.Count);
        Assert.Equal("Transporte", summary.ByCategory[0].Key);
        Assert.Equal(200m, summary.ByCategory[0].Total);
        Assert.Equal("Alimentacao", summary.ByCategory[1].Key);
        Assert.Equal(120m, summary.ByCategory[1].Total);
    }

    [Fact]
    public void Calculate_GroupsByScope_PfVsPj()
    {
        var records = new[]
        {
            Expense(PfAccount, "C", "Pix", 100m),
            Expense(PjAccount, "C", "Pix", 250m),
        };

        var summary = SummaryCalculator.Calculate(records, Scopes);

        Assert.Equal("Business", summary.ByScope[0].Key);
        Assert.Equal(250m, summary.ByScope[0].Total);
        Assert.Equal("Personal", summary.ByScope[1].Key);
        Assert.Equal(100m, summary.ByScope[1].Total);
    }

    [Fact]
    public void Calculate_UnknownAccountScope_FallsBackToUnknown()
    {
        var records = new[] { Expense(Guid.NewGuid(), "C", "Pix", 10m) };

        var summary = SummaryCalculator.Calculate(records, Scopes);

        Assert.Equal("Unknown", summary.ByScope[0].Key);
    }

    [Fact]
    public void Calculate_GroupsByPaymentMethod()
    {
        var records = new[]
        {
            Expense(PfAccount, "C", "Pix", 30m),
            Expense(PfAccount, "C", "CreditCard", 90m),
            Expense(PfAccount, "C", "Pix", 20m),
        };

        var summary = SummaryCalculator.Calculate(records, Scopes);

        Assert.Equal("CreditCard", summary.ByPaymentMethod[0].Key);
        Assert.Equal(90m, summary.ByPaymentMethod[0].Total);
        Assert.Equal("Pix", summary.ByPaymentMethod[1].Key);
        Assert.Equal(50m, summary.ByPaymentMethod[1].Total);
    }
}
