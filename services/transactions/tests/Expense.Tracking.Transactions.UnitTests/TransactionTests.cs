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

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(24)]
    public void Create_CreditCardInInstallments_KeepsTheTotalAmount(int installments)
    {
        var result = Transaction.Create(Owner, AccountId, TransactionType.Expense, PaymentMethod.CreditCard, Guid.NewGuid(),
            "Notebook", "Eletronicos", 3000m, "BRL", Today, null, Now, installments);

        Assert.Equal((installments, 3000m), (result.Value.Installments, result.Value.Amount));
    }

    [Theory]
    [InlineData(PaymentMethod.CreditCard, 0, "transaction.installments_invalid")]
    [InlineData(PaymentMethod.CreditCard, 25, "transaction.installments_invalid")]
    [InlineData(PaymentMethod.Pix, 3, "transaction.installments_not_allowed")]
    [InlineData(PaymentMethod.DebitCard, 2, "transaction.installments_not_allowed")]
    public void Create_WithInvalidInstallments_Fails(PaymentMethod method, int installments, string code)
    {
        var card = method == PaymentMethod.CreditCard ? Guid.NewGuid() : (Guid?)null;

        var result = Transaction.Create(Owner, AccountId, TransactionType.Expense, method, card,
            "Compra", "Compras", 300m, "BRL", Today, null, Now, installments);

        Assert.Equal(code, result.Error.Code);
    }

    // ---------- LNC-3 (ADR-0007): edicao ----------

    private static Transaction Purchase() => Transaction.Create(Owner, AccountId, TransactionType.Expense, PaymentMethod.Pix,
        null, "Mercado", "Alimentacao", 100m, "BRL", Today, null, Now).Value;

    [Fact]
    public void Edit_ChangesTheFields_BumpsTheVersion_AndKeepsIdAccountAndCreation()
    {
        var transaction = Purchase();
        var (id, created, card) = (transaction.Id, transaction.CreatedAtUtc, Guid.NewGuid());

        var result = transaction.Edit(TransactionType.Expense, PaymentMethod.CreditCard, card, " Notebook ", "Eletronicos",
            3000m, "BRL", Today.AddDays(-2), " parcelado ", 6, Now.AddHours(1));

        Assert.True(result.Value);
        Assert.Equal((id, AccountId, created, 2, Now.AddHours(1)),
            (transaction.Id, transaction.AccountId, transaction.CreatedAtUtc, transaction.Version, transaction.UpdatedAtUtc));
        Assert.Equal(("Notebook", "Eletronicos", 3000m, 6, card, "parcelado", Transaction.DescriptionKeyOf("Notebook")),
            (transaction.Description, transaction.Category, transaction.Amount, transaction.Installments,
                transaction.CreditCardId, transaction.Notes, transaction.DescriptionKey));
    }

    [Fact]
    public void Edit_WithoutChanges_KeepsTheVersionAndTheUpdateTime()
    {
        var transaction = Purchase();

        var result = transaction.Edit(TransactionType.Expense, PaymentMethod.Pix, null, "Mercado ", "Alimentacao",
            100m, null, Today, "  ", 1, Now.AddHours(1));

        Assert.False(result.Value);
        Assert.Equal((1, Now), (transaction.Version, transaction.UpdatedAtUtc));
    }

    [Fact]
    public void Edit_FollowsTheCreationRules_AndChangesNothingWhenInvalid()
    {
        var transaction = Purchase();

        var income = transaction.Edit(TransactionType.Income, PaymentMethod.CreditCard, Guid.NewGuid(), "Estorno", "Compras",
            100m, "BRL", Today, null, 3, Now.AddHours(1));
        var empty = transaction.Edit(TransactionType.Expense, PaymentMethod.Pix, null, " ", "Alimentacao",
            100m, "BRL", Today, null, 1, Now.AddHours(1));

        Assert.Equal(("transaction.installments_not_allowed", "transaction.description_required"), (income.Error.Code, empty.Error.Code));
        Assert.Equal(("Mercado", 1), (transaction.Description, transaction.Version));
    }

    [Fact]
    public void Edit_AnImportedTransaction_KeepsItsExternalIdAndBatch()
    {
        var batch = Guid.NewGuid();
        var imported = Transaction.CreateImported(Owner, AccountId, TransactionType.Expense, PaymentMethod.Pix, null,
            "PADARIA", "Padaria", 30m, "BRL", Today, "FIT-1", batch, Now).Value;

        imported.Edit(TransactionType.Expense, PaymentMethod.Pix, null, "Padaria do bairro", "Alimentacao",
            30m, "BRL", Today, null, 1, Now.AddHours(1));

        Assert.Equal(("FIT-1", batch, 2), (imported.ExternalId, imported.ImportBatchId, imported.Version));
    }

    [Fact]
    public void Create_IncomeOnTheCard_IsARefundInFull_NeverInInstallments()
    {
        var refund = Transaction.Create(Owner, AccountId, TransactionType.Income, PaymentMethod.CreditCard, Guid.NewGuid(),
            "Estorno", "Compras", 300m, "BRL", Today, null, Now);
        var split = Transaction.Create(Owner, AccountId, TransactionType.Income, PaymentMethod.CreditCard, Guid.NewGuid(),
            "Estorno", "Compras", 300m, "BRL", Today, null, Now, installments: 3);

        Assert.Equal(1, refund.Value.Installments);
        Assert.Equal("transaction.installments_not_allowed", split.Error.Code);
    }
}
