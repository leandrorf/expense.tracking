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

    // ---------- IMP-6: sugestoes ----------

    [Fact]
    public void CategoryHistory_SuggestsTheCategoryByNormalizedDescription_OnlyForLinesThatCanEnter()
    {
        var history = new Dictionary<string, string> { ["UBER *TRIP"] = "Transporte", ["MERCADO"] = "Mercado" };

        var batch = ImportBatch.Create(Owner, Account, null, PaymentMethod.Pix, ImportFormat.Ofx,
            Statement(StatementKind.BankAccount,
                StatementLine.Valid(1, Day, -30m, "  Uber   *Trip ", "A"),
                StatementLine.Valid(2, Day, -10m, "mercado", "JA-EXISTE"),
                StatementLine.Valid(3, Day, -5m, "PADARIA", "C")),
            new HashSet<string> { "JA-EXISTE" }, Now, history).Value;

        Assert.Equal("Transporte", batch.Lines[0].Category);
        Assert.Null(batch.Lines[1].Category); // duplicada: nao entra, nao precisa de sugestao
        Assert.Null(batch.Lines[2].Category); // sem historico
    }

    [Fact]
    public void CategoryHistory_AlsoSuggestsForACardRefundOutByDefault()
    {
        var batch = ImportBatch.Create(Owner, Account, Guid.NewGuid(), null, ImportFormat.Ofx,
            Statement(StatementKind.CreditCard, StatementLine.Valid(1, Day, 40m, "ESTORNO LOJA", "A")),
            new HashSet<string>(), Now, new Dictionary<string, string> { ["ESTORNO LOJA"] = "Compras" }).Value;

        Assert.Equal(ImportLineStatus.ExcludedByDefault, batch.Lines[0].Status);
        Assert.Equal("Compras", batch.Lines[0].Category);
    }

    [Fact]
    public void BankStatement_MemoHintSuggestsTheMethod_OtherwiseTheBatchDefault()
    {
        var batch = Create(Statement(StatementKind.BankAccount,
                StatementLine.Valid(1, Day, -120m, "PIX ENVIADO MERCADO", "A"),
                StatementLine.Valid(2, Day, -245.90m, "PAG BOLETO ENERGIA", "B"),
                StatementLine.Valid(3, Day, -18.50m, "PADARIA", "C")),
            method: PaymentMethod.DebitCard);

        Assert.Equal(
            [PaymentMethod.Pix, PaymentMethod.Boleto, PaymentMethod.DebitCard],
            batch.Lines.Select(l => l.PaymentMethod!.Value));
    }

    [Fact]
    public void CardStatement_IgnoresMemoHints()
    {
        var batch = Create(Statement(StatementKind.CreditCard, StatementLine.Valid(1, Day, -10m, "PIX LOJA", "A")),
            card: Guid.NewGuid(), method: null);

        Assert.Equal(PaymentMethod.CreditCard, batch.Lines[0].PaymentMethod);
    }

    [Fact]
    public void TransactionDescriptionKey_IsTheNormalizedDescription()
    {
        var transaction = Transaction.Create(Owner, Account, TransactionType.Expense, PaymentMethod.Pix, null,
            " Uber   *Trip ", "Transporte", 30m, "BRL", Day, null, Now).Value;

        Assert.Equal("UBER *TRIP", transaction.DescriptionKey);
        Assert.Equal(transaction.DescriptionKey, Transaction.DescriptionKeyOf("uber *trip"));
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

    private static ImportLineDecision Category(ImportLine line, string category, bool? included = null) =>
        new(line.Id, included, category, null);

    [Fact]
    public void Confirm_CreatesATransactionPerIncludedLine_WithExternalIdAndBatch()
    {
        var batch = Create(Statement(StatementKind.BankAccount,
            StatementLine.Valid(1, Day, -18.50m, "PADARIA", "A"),
            StatementLine.Valid(2, Day, 5000m, "SALARIO", "B")));

        var result = batch.Confirm(
            [Category(batch.Lines[0], " Alimentacao "), new ImportLineDecision(batch.Lines[1].Id, null, "Salario", PaymentMethod.BankTransfer)],
            new HashSet<string>(), Now);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Description : null);
        Assert.Equal(ImportBatchStatus.Confirmed, batch.Status);
        Assert.Equal(Now, batch.ConfirmedAtUtc);

        var (expense, income) = (result.Value.Transactions[0], result.Value.Transactions[1]);
        Assert.Equal((TransactionType.Expense, 18.50m, "Alimentacao", PaymentMethod.Pix), (expense.Type, expense.Amount, expense.Category, expense.PaymentMethod));
        Assert.Equal((TransactionType.Income, PaymentMethod.BankTransfer), (income.Type, income.PaymentMethod));
        Assert.Equal(("A", batch.Id, Account, Owner), (expense.ExternalId, expense.ImportBatchId, expense.AccountId, expense.OwnerUserId));
        Assert.Equal(Day, expense.OccurredOn);
        Assert.Null(expense.CreditCardId);
        Assert.All(batch.Lines, l => Assert.Equal(ImportLineStatus.Imported, l.Status));
        Assert.Equal(expense.Id, batch.Lines[0].TransactionId);
    }

    [Fact]
    public void Confirm_CardStatement_LinksTheCardAndCanIncludeARefundAsIncome()
    {
        var card = Guid.NewGuid();
        var batch = Create(Statement(StatementKind.CreditCard,
                StatementLine.Valid(1, Day, -89.90m, "SOFTWARE", "A"),
                StatementLine.Valid(2, Day, 30m, "ESTORNO", "B")),
            card: card, method: null);

        var created = batch.Confirm(
            [Category(batch.Lines[0], "Software"), Category(batch.Lines[1], "Software", included: true)],
            new HashSet<string>(), Now).Value.Transactions;

        Assert.Equal(2, created.Count);
        Assert.All(created, t => Assert.Equal((PaymentMethod.CreditCard, card), (t.PaymentMethod, t.CreditCardId!.Value)));
        Assert.Equal(TransactionType.Income, created[1].Type);
    }

    [Fact]
    public void Confirm_IncludedLinesWithoutCategory_FailsListingTheLinesAndChangesNothing()
    {
        var batch = Create(Statement(StatementKind.BankAccount,
            StatementLine.Valid(1, Day, -1m, "A", "A"),
            StatementLine.Valid(2, Day, -1m, "B", "B"),
            StatementLine.Valid(3, Day, -1m, "C", "C")));

        var result = batch.Confirm([Category(batch.Lines[1], "Mercado")], new HashSet<string>(), Now);

        Assert.True(result.IsFailure);
        Assert.Equal("import.categories_required", result.Error.Code);
        Assert.Equal(new[] { 1, 3 }, (IEnumerable<int>)result.Error.Metadata!["lines"]!);
        Assert.Equal(ImportBatchStatus.Pending, batch.Status);
        Assert.All(batch.Lines, l => Assert.Null(l.TransactionId));
    }

    [Fact]
    public void Confirm_ExcludedLinesDoNotNeedCategory()
    {
        var batch = Create(Statement(StatementKind.BankAccount,
            StatementLine.Valid(1, Day, -1m, "A", "A"),
            StatementLine.Valid(2, Day, -1m, "B", "B")));

        var created = batch.Confirm(
            [Category(batch.Lines[0], "Mercado"), new ImportLineDecision(batch.Lines[1].Id, false, null, null)],
            new HashSet<string>(), Now).Value.Transactions;

        Assert.Single(created);
        Assert.Equal(ImportLineStatus.New, batch.Lines[1].Status);
        Assert.False(batch.Lines[1].Included);
    }

    [Fact]
    public void Confirm_KeyLaunchedAfterThePreview_BecomesDuplicateInsteadOfTransaction()
    {
        var batch = Create(Statement(StatementKind.BankAccount,
            StatementLine.Valid(1, Day, -1m, "A", "A"),
            StatementLine.Valid(2, Day, -1m, "B", "B")));

        var created = batch.Confirm(
            [Category(batch.Lines[0], "X"), Category(batch.Lines[1], "X")], new HashSet<string> { "B" }, Now).Value.Transactions;

        Assert.Single(created);
        Assert.Equal(ImportLineStatus.Duplicate, batch.Lines[1].Status);
        Assert.False(batch.Lines[1].Included);
    }

    [Fact]
    public void Confirm_TryingToIncludeADuplicateOrInvalidLine_Fails()
    {
        var batch = Create(Statement(StatementKind.BankAccount,
                StatementLine.Valid(1, Day, -1m, "A", "JA-EXISTE"),
                StatementLine.Invalid(2, "B", "Data invalida")),
            existing: "JA-EXISTE");

        Assert.Equal("import.line_not_includable",
            batch.Confirm([Category(batch.Lines[0], "X", included: true)], new HashSet<string>(), Now).Error.Code);
        Assert.Equal("import.line_not_includable",
            batch.Confirm([Category(batch.Lines[1], "X", included: true)], new HashSet<string>(), Now).Error.Code);
    }

    [Theory]
    [InlineData(StatementKind.BankAccount, PaymentMethod.CreditCard)]
    [InlineData(StatementKind.CreditCard, PaymentMethod.Pix)]
    public void Confirm_PaymentMethodAgainstTheStatementKind_Fails(StatementKind kind, PaymentMethod method)
    {
        var batch = Create(Statement(kind, StatementLine.Valid(1, Day, -1m, "A", "A")),
            card: kind == StatementKind.CreditCard ? Guid.NewGuid() : null,
            method: kind == StatementKind.CreditCard ? null : PaymentMethod.Pix);

        var result = batch.Confirm([new ImportLineDecision(batch.Lines[0].Id, null, "X", method)], new HashSet<string>(), Now);

        Assert.Equal("import.payment_method_invalid", result.Error.Code);
    }

    [Fact]
    public void Confirm_UnknownLineOrAlreadyConfirmed_Fails()
    {
        var batch = Create(Statement(StatementKind.BankAccount, StatementLine.Valid(1, Day, -1m, "A", "A")));

        Assert.Equal("import.line_not_found",
            batch.Confirm([new ImportLineDecision(Guid.NewGuid(), true, "X", null)], new HashSet<string>(), Now).Error.Code);

        Assert.True(batch.Confirm([Category(batch.Lines[0], "X")], new HashSet<string>(), Now).IsSuccess);
        Assert.Equal("import.not_pending", batch.Confirm([], new HashSet<string>(), Now).Error.Code);
    }

    // ---------- IMP-9: desfazer ----------

    [Fact]
    public void Undo_ConfirmedBatch_IsUndone_KeepingTheLinesAsRecord()
    {
        var batch = Create(Statement(StatementKind.BankAccount, StatementLine.Valid(1, Day, -1m, "A", "A")));
        batch.Confirm([Category(batch.Lines[0], "X")], new HashSet<string>(), Now);
        var later = Now + TimeSpan.FromHours(1);

        Assert.True(batch.Undo(later).IsSuccess);

        Assert.Equal(ImportBatchStatus.Undone, batch.Status);
        Assert.Equal(later, batch.UndoneAtUtc);
        Assert.Equal(ImportLineStatus.Imported, batch.Lines[0].Status);
        Assert.False(batch.IsExpired(Now + TimeSpan.FromDays(30)));
    }

    [Fact]
    public void Undo_PendingOrAlreadyUndoneBatch_Fails_AndAnUndoneBatchCannotBeConfirmed()
    {
        var batch = Create(Statement(StatementKind.BankAccount, StatementLine.Valid(1, Day, -1m, "A", "A")));

        Assert.Equal("import.not_confirmed", batch.Undo(Now).Error.Code);
        Assert.Equal(ImportBatchStatus.Pending, batch.Status);

        batch.Confirm([Category(batch.Lines[0], "X")], new HashSet<string>(), Now);
        batch.Undo(Now);
        Assert.Equal("import.not_confirmed", batch.Undo(Now).Error.Code);
        Assert.Equal("import.not_pending", batch.Confirm([], new HashSet<string>(), Now).Error.Code);
    }

    [Fact]
    public void PendingBatch_ExpiresAfterSevenDays()
    {
        var batch = Create(Statement(StatementKind.BankAccount));

        Assert.False(batch.IsExpired(Now + TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1)));
        Assert.True(batch.IsExpired(Now + TimeSpan.FromDays(7)));
    }
}
