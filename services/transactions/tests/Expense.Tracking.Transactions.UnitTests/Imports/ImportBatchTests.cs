using Expense.Tracking.Transactions.Domain.Imports;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.UnitTests.Imports;

/// <summary>IMP-4: regras da previa (ADR-0004, decisoes 2, 3, 5 e 6).</summary>
public sealed class ImportBatchTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-21T12:00:00Z");
    private static readonly DateOnly Day = new(2026, 10, 2);

    private static ParsedStatement Statement(StatementKind kind, params StatementLine[] lines) => new(kind, "BRL", lines);

    private static ImportBatch Create(
        ParsedStatement statement, Guid? card = null, PaymentMethod? method = PaymentMethod.Pix, params string[] existing) =>
        ImportBatch.Create(Owner, Account, card, method, ImportFormat.Ofx, statement, existing.ToHashSet(), Now).Value;

    [Fact]
    public void BankStatement_DebitIsExpenseAndCreditIsIncome_BothIncludedWithTheDefaultMethod()
    {
        var batch = Create(Statement(StatementKind.BankAccount,
            StatementLine.Valid(1, Day, -18.50m, "PADARIA", "A"),
            StatementLine.Valid(2, Day, 5000m, "SALARIO", "B")));

        Assert.Equal(ImportBatchStatus.Pending, batch.Status);
        Assert.Equal(Now + ImportBatch.PendingLifetime, batch.ExpiresAtUtc);
        Assert.Equal(PaymentMethod.Pix, batch.DefaultPaymentMethod);

        var (expense, income) = (batch.Lines[0], batch.Lines[1]);
        Assert.Equal((TransactionType.Expense, 18.50m, true), (expense.Type!.Value, expense.Amount!.Value, expense.Included));
        Assert.Equal((TransactionType.Income, 5000m, true), (income.Type!.Value, income.Amount!.Value, income.Included));
        Assert.All(batch.Lines, l => Assert.Equal(ImportLineStatus.New, l.Status));
        Assert.All(batch.Lines, l => Assert.Equal(PaymentMethod.Pix, l.PaymentMethod));
        Assert.All(batch.Lines, l => Assert.Null(l.Category));
    }

    [Fact]
    public void CardStatement_UsesCreditCard_AndCreditIsOutByDefault()
    {
        var batch = Create(Statement(StatementKind.CreditCard,
                StatementLine.Valid(1, Day, -89.90m, "SOFTWARE", "A"),
                StatementLine.Valid(2, Day, 1250m, "PAGAMENTO DE FATURA", "B")),
            card: Guid.NewGuid(), method: null);

        Assert.Equal(PaymentMethod.CreditCard, batch.DefaultPaymentMethod);
        Assert.Equal(ImportLineStatus.New, batch.Lines[0].Status);
        Assert.Equal(ImportLineStatus.ExcludedByDefault, batch.Lines[1].Status);
        Assert.False(batch.Lines[1].Included);
        Assert.Equal(TransactionType.Income, batch.Lines[1].Type);
    }

    [Fact]
    public void LineAlreadyLaunched_IsDuplicateAndNotIncluded()
    {
        var batch = Create(Statement(StatementKind.BankAccount,
                StatementLine.Valid(1, Day, -1m, "A", "JA-EXISTE"),
                StatementLine.Valid(2, Day, -1m, "B", "NOVA")),
            existing: "JA-EXISTE");

        Assert.Equal(ImportLineStatus.Duplicate, batch.Lines[0].Status);
        Assert.False(batch.Lines[0].Included);
        Assert.Equal(ImportLineStatus.New, batch.Lines[1].Status);
    }

    [Fact]
    public void InvalidLine_KeepsTheProblemAndIsNotIncluded()
    {
        var batch = Create(Statement(StatementKind.BankAccount, StatementLine.Invalid(7, "X", "Data invalida")));

        var line = Assert.Single(batch.Lines);
        Assert.Equal((ImportLineStatus.Invalid, false, "Data invalida", 7), (line.Status, line.Included, line.Problem, line.Number));
        Assert.Null(line.Amount);
    }

    [Fact]
    public void LongDescription_IsCutToTheTransactionLimit_AndLongExternalIdIsInvalid()
    {
        var batch = Create(Statement(StatementKind.BankAccount,
            StatementLine.Valid(1, Day, -1m, new string('x', 250), "A"),
            StatementLine.Valid(2, Day, -1m, "B", new string('9', ImportLine.ExternalIdMaxLength + 1))));

        Assert.Equal(Transaction.DescriptionMaxLength, batch.Lines[0].Description.Length);
        Assert.Equal(ImportLineStatus.Invalid, batch.Lines[1].Status);
    }

    [Theory]
    [InlineData(StatementKind.CreditCard, false, PaymentMethod.Pix, "import.card_required")]
    [InlineData(StatementKind.BankAccount, true, PaymentMethod.Pix, "import.card_not_allowed")]
    [InlineData(StatementKind.BankAccount, false, null, "import.payment_method_required")]
    [InlineData(StatementKind.BankAccount, false, PaymentMethod.CreditCard, "import.payment_method_invalid")]
    public void CardAndMethodRules_AreEnforced(StatementKind kind, bool withCard, PaymentMethod? method, string code)
    {
        var result = ImportBatch.Create(Owner, Account, withCard ? Guid.NewGuid() : null, method, ImportFormat.Ofx,
            Statement(kind), new HashSet<string>(), Now);

        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
    }

    [Fact]
    public void MoreThanTheLineLimit_IsRejected()
    {
        var lines = Enumerable.Range(1, ImportBatch.MaxLines + 1)
            .Select(i => StatementLine.Valid(i, Day, -1m, "X", $"F{i}"))
            .ToArray();

        var result = ImportBatch.Create(Owner, Account, null, PaymentMethod.Pix, ImportFormat.Ofx,
            Statement(StatementKind.BankAccount, lines), new HashSet<string>(), Now);

        Assert.Equal("import.too_many_lines", result.Error.Code);
    }

    [Fact]
    public void PendingBatch_ExpiresAfterSevenDays()
    {
        var batch = Create(Statement(StatementKind.BankAccount));

        Assert.False(batch.IsExpired(Now + TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1)));
        Assert.True(batch.IsExpired(Now + TimeSpan.FromDays(7)));
    }
}
