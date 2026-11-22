using Expense.Tracking.Transactions.Domain.AccountingExport;

namespace Expense.Tracking.Transactions.UnitTests.AccountingExport;

/// <summary>CTB-5 (ADR-0006, decisao 7): categoria associada a uma conta do plano do contador.</summary>
public sealed class LedgerAccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_TrimsCategoryCodeAndName_AndAnEmptyNameIsNull()
    {
        var account = LedgerAccount.Create(Guid.NewGuid(), "  Software ", " 3.1.02 ", "  ", Now).Value;

        Assert.Equal(("Software", "3.1.02", (string?)null), (account.Category, account.Code, account.Name));
    }

    [Theory]
    [InlineData(null, "3.1", "ledger_account.category_required")]
    [InlineData("Software", " ", "ledger_account.code_required")]
    [InlineData("Software", "123456789012345678901", "ledger_account.code_too_long")]
    public void Create_RejectsInvalidInput(string? category, string? code, string expected) =>
        Assert.Equal(expected, LedgerAccount.Create(Guid.NewGuid(), category, code, null, Now).Error.Code);

    [Fact]
    public void Create_RejectsLongCategoryOrName()
    {
        Assert.Equal("ledger_account.category_too_long",
            LedgerAccount.Create(Guid.NewGuid(), new string('c', 61), "1", null, Now).Error.Code);
        Assert.Equal("ledger_account.name_too_long",
            LedgerAccount.Create(Guid.NewGuid(), "Software", "1", new string('n', 101), Now).Error.Code);
    }

    [Fact]
    public void Change_SwapsCodeAndName_AndKeepsTheOldOnesWhenInvalid()
    {
        var account = LedgerAccount.Create(Guid.NewGuid(), "Software", "3.1.02", "Software", Now).Value;

        Assert.True(account.Change("3.1.09", "Licencas", Now.AddDays(1)).IsSuccess);
        Assert.False(account.Change("", null, Now.AddDays(2)).IsSuccess);

        Assert.Equal(("3.1.09", "Licencas", Now.AddDays(1)), (account.Code, account.Name, account.UpdatedAtUtc));
    }
}
