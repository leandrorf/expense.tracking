using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.UnitTests;

public sealed class TransactionTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 1, 15);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-15T10:00:00Z");

    private static Result<Transaction> CreateCash(decimal amount = 100m) =>
        Transaction.Create(Owner, AccountId, TransactionType.Expense, PaymentMethod.Pix, null,
            "Almoco", "Alimentacao", amount, "BRL", Today, null, Now);

    [Fact]
    public void Create_ValidPix_Succeeds()
    {
        var result = CreateCash();

        Assert.True(result.IsSuccess);
        Assert.Equal("Alimentacao", result.Value.Category);
        Assert.Equal(100m, result.Value.Amount);
        Assert.Equal("BRL", result.Value.Currency);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_WithNonPositiveAmount_Fails(decimal amount)
    {
        var result = Transaction.Create(Owner, AccountId, TransactionType.Expense, PaymentMethod.Cash, null,
            "X", "Cat", amount, "BRL", Today, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("money.amount_invalid", result.Error.Code);
    }

    [Fact]
    public void Create_WithTooManyDecimals_Fails()
    {
        var result = Transaction.Create(Owner, AccountId, TransactionType.Expense, PaymentMethod.Cash, null,
            "X", "Cat", 10.999m, "BRL", Today, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("money.amount_precision", result.Error.Code);
    }

    [Fact]
    public void Create_CreditCardWithoutCardId_Fails()
    {
        var result = Transaction.Create(Owner, AccountId, TransactionType.Expense, PaymentMethod.CreditCard, null,
            "Compra", "Cat", 50m, "BRL", Today, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("transaction.card_required", result.Error.Code);
    }

    [Fact]
    public void Create_NonCreditWithCardId_Fails()
    {
        var result = Transaction.Create(Owner, AccountId, TransactionType.Expense, PaymentMethod.Pix, Guid.NewGuid(),
            "Compra", "Cat", 50m, "BRL", Today, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("transaction.card_not_allowed", result.Error.Code);
    }

    [Fact]
    public void Create_CreditCardWithCardId_Succeeds()
    {
        var result = Transaction.Create(Owner, AccountId, TransactionType.Expense, PaymentMethod.CreditCard, Guid.NewGuid(),
            "Compra", "Cat", 50m, "BRL", Today, null, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentMethod.CreditCard, result.Value.PaymentMethod);
        Assert.NotNull(result.Value.CreditCardId);
    }

    [Fact]
    public void Create_WithoutDescription_Fails()
    {
        var result = Transaction.Create(Owner, AccountId, TransactionType.Expense, PaymentMethod.Cash, null,
            "  ", "Cat", 10m, "BRL", Today, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("transaction.description_required", result.Error.Code);
    }
}
