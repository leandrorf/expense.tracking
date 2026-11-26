using Expense.Tracking.Transactions.Domain.Imports;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.UnitTests.Imports;

/// <summary>INV-11 (ADR-0005, decisao 10): pagamento de fatura reconhecido no extrato importado.</summary>
public sealed class InvoicePaymentImportTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-21T12:00:00Z");
    private static readonly DateOnly PaidOn = new(2026, 11, 16);

    [Theory]
    [InlineData("PAG FATURA CARTAO")]
    [InlineData("Pagamento de fatura - Nubank")]
    [InlineData("PGTO CARTÃO CRÉDITO")]
    [InlineData("pagto.fatura visa")]
    [InlineData("DEB AUTOMATICO PAGAMENTO FATURA")]
    public void Hints_RecognizeInvoicePayments(string memo) => Assert.True(InvoicePaymentHints.Matches(memo));

    [Theory]
    [InlineData("PIX ENVIADO MERCADO")]
    [InlineData("PAGAMENTO BOLETO ENERGIA")]
    [InlineData("FATURAMENTO LOJA")]
    [InlineData("PAGFATURA")]
    [InlineData("")]
    public void Hints_IgnoreOtherLines(string memo) => Assert.False(InvoicePaymentHints.Matches(memo));

    [Theory]
    // Vence dia 17: pago em 16/11 ou atrasado em 25/11, a de novembro; em 10/12, a de dezembro.
    [InlineData("2026-11-16", 17, "2026-11-01")]
    [InlineData("2026-11-25", 17, "2026-11-01")]
    [InlineData("2026-12-10", 17, "2026-12-01")]
    // Vence dia 5: pago em 04/01 quita a de janeiro (virada de ano).
    [InlineData("2027-01-04", 5, "2027-01-01")]
    [InlineData("2026-12-30", 5, "2027-01-01")]
    public void SuggestedInvoice_IsTheOneWithTheClosestDueDate(string paidOn, int dueDay, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), InvoicePaymentHints.SuggestDueMonth(DateOnly.Parse(paidOn), dueDay));

    private static ImportBatch Preview(IReadOnlyList<InvoiceCardHint> cards, StatementKind kind = StatementKind.BankAccount) =>
        ImportBatch.Create(
            Owner, Account, kind == StatementKind.CreditCard ? Guid.NewGuid() : null,
            kind == StatementKind.CreditCard ? null : PaymentMethod.Pix, ImportFormat.Ofx,
            new ParsedStatement(kind, "BRL",
            [
                StatementLine.Valid(1, PaidOn, -1500m, "PAG FATURA CARTAO", "A"),
                StatementLine.Valid(2, PaidOn, -30m, "PIX ENVIADO PADARIA", "B"),
                StatementLine.Valid(3, PaidOn, 200m, "ESTORNO PAG FATURA", "C"),
            ]),
            new HashSet<string>(), Now, accountCards: cards).Value;

    [Fact]
    public void Preview_WithOneCardOnTheAccount_SuggestsThePaymentOfItsClosestInvoice()
    {
        var card = Guid.NewGuid();

        var batch = Preview([new InvoiceCardHint(card, DueDay: 17)]);

        var payment = batch.Lines[0];
        Assert.Equal((true, true, card, new DateOnly(2026, 11, 1)),
            (payment.InvoicePayment, payment.Included, payment.InvoiceCardId, payment.InvoiceDueMonth));
        Assert.False(batch.Lines[1].InvoicePayment);
        // Entrada com a pista nao e pagamento de fatura (so saida de conta paga fatura).
        Assert.False(batch.Lines[2].InvoicePayment);
    }

    [Fact]
    public void Preview_WithSeveralCards_SuggestsThePaymentButLeavesTheCardToTheUser()
    {
        var batch = Preview([new InvoiceCardHint(Guid.NewGuid(), 17), new InvoiceCardHint(Guid.NewGuid(), 5)]);

        Assert.True(batch.Lines[0].InvoicePayment);
        Assert.Null(batch.Lines[0].InvoiceCardId);
        Assert.Null(batch.Lines[0].InvoiceDueMonth);
    }

    [Fact]
    public void CardStatement_NeverSuggestsAnInvoicePayment()
    {
        var batch = Preview([new InvoiceCardHint(Guid.NewGuid(), 17)], StatementKind.CreditCard);

        Assert.All(batch.Lines, l => Assert.False(l.InvoicePayment));
    }

    [Fact]
    public void Confirm_TurnsTheSuggestedLineIntoACardPayment_WithoutCategory_AndTheRestIntoTransactions()
    {
        var card = Guid.NewGuid();
        var batch = Preview([new InvoiceCardHint(card, 17)]);

        var result = batch.Confirm(
            [new ImportLineDecision(batch.Lines[1].Id, null, "Padaria", null),
             new ImportLineDecision(batch.Lines[2].Id, false, null, null)],
            new HashSet<string>(), Now);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Description : null);
        var payment = Assert.Single(result.Value.Payments);
        Assert.Equal((card, new DateOnly(2026, 11, 1), Account, 1500m, PaidOn, "A", batch.Id),
            (payment.CreditCardId, payment.InvoiceDueMonth, payment.AccountId, payment.Amount, payment.PaidOn,
             payment.ExternalId, payment.ImportBatchId));
        Assert.Equal("Padaria", Assert.Single(result.Value.Transactions).Category);
        Assert.Equal((ImportLineStatus.Imported, payment.Id, (Guid?)null),
            (batch.Lines[0].Status, batch.Lines[0].CardPaymentId, batch.Lines[0].TransactionId));
    }

    [Fact]
    public void Confirm_TheUserCanPickTheCardAndInvoice_OrTurnTheLineIntoAnExpense()
    {
        var chosen = Guid.NewGuid();
        var batch = Preview([new InvoiceCardHint(Guid.NewGuid(), 17), new InvoiceCardHint(Guid.NewGuid(), 5)]);
        var line = batch.Lines[0];

        var missing = batch.Confirm([new ImportLineDecision(batch.Lines[1].Id, false, null, null),
                new ImportLineDecision(batch.Lines[2].Id, false, null, null)], new HashSet<string>(), Now);
        Assert.Equal("import.invoice_required", missing.Error.Code);
        Assert.Equal(new[] { 1 }, missing.Error.Metadata!["lines"]);

        var picked = batch.Confirm(
            [new ImportLineDecision(line.Id, null, null, null, InvoiceCardId: chosen, InvoiceDueMonth: new DateOnly(2026, 10, 20)),
             new ImportLineDecision(batch.Lines[1].Id, false, null, null),
             new ImportLineDecision(batch.Lines[2].Id, false, null, null)],
            new HashSet<string>(), Now);
        Assert.Equal((chosen, new DateOnly(2026, 10, 1)), (picked.Value.Payments[0].CreditCardId, picked.Value.Payments[0].InvoiceDueMonth));
    }

    [Fact]
    public void Confirm_LineSwitchedToAnExpense_NeedsACategory_AndBecomesATransaction()
    {
        var batch = Preview([new InvoiceCardHint(Guid.NewGuid(), 17)]);
        var line = batch.Lines[0];
        var others = new[]
        {
            new ImportLineDecision(batch.Lines[1].Id, false, null, null),
            new ImportLineDecision(batch.Lines[2].Id, false, null, null),
        };

        var noCategory = batch.Confirm([new ImportLineDecision(line.Id, null, null, null, InvoicePayment: false), .. others],
            new HashSet<string>(), Now);
        Assert.Equal("import.categories_required", noCategory.Error.Code);

        var asExpense = batch.Confirm([new ImportLineDecision(line.Id, null, "Cartao", null, InvoicePayment: false), .. others],
            new HashSet<string>(), Now);
        Assert.Empty(asExpense.Value.Payments);
        Assert.Equal((1500m, TransactionType.Expense), (asExpense.Value.Transactions[0].Amount, asExpense.Value.Transactions[0].Type));
    }

    [Fact]
    public void Confirm_IncomeMarkedAsInvoicePayment_IsRejected()
    {
        var batch = Preview([]);

        var result = batch.Confirm([new ImportLineDecision(batch.Lines[2].Id, null, null, null, InvoicePayment: true)],
            new HashSet<string>(), Now);

        Assert.Equal("import.invoice_payment_invalid", result.Error.Code);
    }

    [Fact]
    public void SuggestDueMonth_UsesTheDueDayOfTheCycleOfEachMonth()
    {
        // CYC-6: vence dia 17 hoje; a partir da fatura de fev/2027, vence dia 5.
        var card = new InvoiceCardHint(Guid.NewGuid(), 17,
            [new CardDueDayFrom(null, 17), new CardDueDayFrom(new DateOnly(2027, 2, 1), 5)]);
        var paidOn = new DateOnly(2027, 1, 30);

        Assert.Equal(new DateOnly(2027, 2, 1), InvoicePaymentHints.SuggestDueMonth(paidOn, card.DueDayOf));
        Assert.Equal(new DateOnly(2027, 1, 1), InvoicePaymentHints.SuggestDueMonth(paidOn, card.DueDay));
        Assert.Equal((17, 5), (card.DueDayOf(new DateOnly(2027, 1, 1)), card.DueDayOf(new DateOnly(2027, 3, 1))));
    }
}
