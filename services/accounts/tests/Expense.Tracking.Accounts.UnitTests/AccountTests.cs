using Expense.Tracking.Accounts.Domain.Accounts;

namespace Expense.Tracking.Accounts.UnitTests;

public sealed class AccountTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public void Create_Personal_DefaultsCurrencyToBrl()
    {
        var result = Account.Create(Owner, "Conta Pessoal", AccountScope.Personal, null, null, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("BRL", result.Value.Currency);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public void Create_WithoutName_Fails()
    {
        var result = Account.Create(Owner, "   ", AccountScope.Personal, null, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("account.name_required", result.Error.Code);
    }

    [Fact]
    public void Create_Business_RequiresCnpj_RejectsCpf()
    {
        var result = Account.Create(Owner, "Minha Empresa", AccountScope.Business, "529.982.247-25", null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("account.taxid_scope_mismatch", result.Error.Code);
    }

    [Fact]
    public void Create_Business_WithCnpj_Succeeds_StoresDigitsOnly()
    {
        var result = Account.Create(Owner, "Minha Empresa", AccountScope.Business, "11.222.333/0001-81", null, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("11222333000181", result.Value.TaxId);
    }

    [Fact]
    public void Create_WithOwnerEmpty_Fails()
    {
        var result = Account.Create(Guid.Empty, "X", AccountScope.Personal, null, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("account.owner_required", result.Error.Code);
    }

    [Fact]
    public void Rename_UpdatesNameAndTimestamp()
    {
        var account = Account.Create(Owner, "Antigo", AccountScope.Personal, null, null, Now).Value;

        var later = Now.AddHours(1);
        var result = account.Rename("Novo", later);

        Assert.True(result.IsSuccess);
        Assert.Equal("Novo", account.Name);
        Assert.Equal(later, account.UpdatedAtUtc);
    }

    // ---------- INV-13: saldo inicial ----------

    private static readonly DateOnly Today = new(2026, 1, 1);

    [Fact]
    public void SetOpeningBalance_KeepsAmountAndDate_AcceptsNegative_AndClearRemovesIt()
    {
        var account = Account.Create(Owner, "Conta", AccountScope.Personal, null, null, Now).Value;
        var later = Now.AddHours(2);

        var set = account.SetOpeningBalance(-150.25m, Today.AddDays(-3), Today, later);

        Assert.True(set.IsSuccess);
        Assert.Equal((-150.25m, Today.AddDays(-3), later), (account.OpeningBalance, account.OpeningBalanceOn, account.UpdatedAtUtc));

        account.ClearOpeningBalance(later.AddHours(1));
        Assert.Equal(((decimal?)null, (DateOnly?)null), (account.OpeningBalance, account.OpeningBalanceOn));
    }

    [Theory]
    [InlineData("10.555", 0, "account.opening_balance_invalid")]
    [InlineData("1000000000000", 0, "account.opening_balance_invalid")]
    [InlineData("10", 1, "account.opening_balance_date_future")]
    public void SetOpeningBalance_RejectsMoreThanTwoDecimals_HugeValues_AndFutureDates(string amount, int daysAhead, string code)
    {
        var account = Account.Create(Owner, "Conta", AccountScope.Personal, null, null, Now).Value;

        var set = account.SetOpeningBalance(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture),
            Today.AddDays(daysAhead), Today, Now);

        Assert.Equal(code, set.Error.Code);
        Assert.Null(account.OpeningBalance);
    }
}
