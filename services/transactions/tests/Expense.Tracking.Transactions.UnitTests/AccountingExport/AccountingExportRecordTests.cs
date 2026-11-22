using Expense.Tracking.Transactions.Domain.AccountingExport;

namespace Expense.Tracking.Transactions.UnitTests.AccountingExport;

/// <summary>CTB-6 (ADR-0006, decisao 8): registro da exportacao, com totais e impressao digital.</summary>
public sealed class AccountingExportRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fingerprint_IsStableForTheSameSheets_AndChangesWithAnyOfThem()
    {
        var original = AccountingExportRecord.FingerprintOf("a;b\r\n", "c\r\n", "d\r\n");

        Assert.Equal(64, original.Length);
        Assert.Equal(original, AccountingExportRecord.FingerprintOf("a;b\r\n", "c\r\n", "d\r\n"));
        Assert.NotEqual(original, AccountingExportRecord.FingerprintOf("a;b\r\n", "c\r\n", "e\r\n"));
        // A fronteira entre planilhas conta: mover texto de uma para a outra muda a impressao.
        Assert.NotEqual(original, AccountingExportRecord.FingerprintOf("a;b\r\nc\r\n", "", "d\r\n"));
    }

    [Fact]
    public void Create_WithoutAccount_MeansAllBusinessAccounts_AndUpdateReplacesTheNumbers()
    {
        var record = AccountingExportRecord.Create(Guid.NewGuid(), new DateOnly(2026, 10, 1), null, 3, 1,
            [new AccountingExportTotal("BRL", 100m, 30m)], "abc", Now);

        record.Update(4, 0, [new AccountingExportTotal("BRL", 100m, 80m)], "def", Now.AddDays(1));

        Assert.Equal((AccountingExportRecord.AllAccounts, 4, 0, "def", Now.AddDays(1), 80m),
            (record.AccountId, record.Lines, record.Payments, record.Fingerprint, record.ExportedAtUtc, record.Totals[0].Expense));
    }
}
