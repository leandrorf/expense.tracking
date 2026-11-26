using Expense.Tracking.Reporting.Domain.Invoices;

namespace Expense.Tracking.Reporting.UnitTests;

/// <summary>RCN-2 (ADR-0011, decisao 7): a conferencia guardada de uma fatura.</summary>
public sealed class InvoiceReconciliationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-26T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Create_KeepsTheMonthStart_AndTheAcceptedDifference()
    {
        var r = InvoiceReconciliation.Create(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 12, 17), 3214.50m, 3180m, Now).Value;

        Assert.Equal((new DateOnly(2026, 12, 1), 34.50m, Now), (r.DueMonth, r.Difference, r.ReconciledAtUtc));
    }

    [Theory]
    [InlineData(10.123)]
    [InlineData(1000000000.01)]
    public void Create_RefusesTotalsWithMoreThanTwoDecimalsOrTooBig(double bankTotal)
    {
        var created = InvoiceReconciliation.Create(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 12, 1), (decimal)bankTotal, 0m, Now);

        Assert.Equal("reconciliation.bank_total_invalid", created.Error.Code);
    }

    [Fact]
    public void Update_ReplacesTotalsAndDate()
    {
        var r = InvoiceReconciliation.Create(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 12, 1), 100m, 90m, Now).Value;

        r.Update(95m, 95m, Now.AddDays(1));

        Assert.Equal((0m, Now.AddDays(1)), (r.Difference, r.ReconciledAtUtc));
    }
}
