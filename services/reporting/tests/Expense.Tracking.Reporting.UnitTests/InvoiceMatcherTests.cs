using Expense.Tracking.Reporting.Domain.Invoices;

namespace Expense.Tracking.Reporting.UnitTests;

/// <summary>RCN-4 (ADR-0011, decisoes 3 e 4): o casamento das linhas do banco com os itens da fatura.</summary>
public sealed class InvoiceMatcherTests
{
    private static int _number;

    private static BankLine Line(string date, decimal amount, string description, Guid? known = null) =>
        new(Guid.NewGuid(), ++_number, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), amount, description, known);

    private static InvoiceItem Item(string date, decimal amount, string description, int installment = 1, int installments = 1, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), "Compras", DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), amount, installment, installments, description);

    private static ReconciliationEntry Of(IReadOnlyList<ReconciliationEntry> entries, BankLine line) => Assert.Single(entries, e => e.Line == line);

    [Fact]
    public void Key_MatchesTheSameTransaction_EvenWithAnotherDateOrAmount()
    {
        var id = Guid.NewGuid();
        var item = Item("2026-11-12", 100m, "Mercado", id: id);
        var line = Line("2026-11-20", 120m, "SUPERMERCADO X", known: id);

        var entry = Of(InvoiceMatcher.Match([line], [item]), line);

        Assert.Equal((ReconciliationGroup.DifferentAmount, MatchRule.Key, item, 20m), (entry.Group, entry.Rule, entry.Item, entry.Explains));
    }

    [Fact]
    public void Installment_MatchesTheInstallmentOfTheInvoice_WithoutLookingAtTheDate()
    {
        var notebook = Item("2026-09-15", 300m, "Notebook Loja", installment: 3, installments: 10);
        var line = Line("2026-11-15", 300m, "LOJA NOTEBOOK PARCELA 03/10");

        var entry = Of(InvoiceMatcher.Match([line], [notebook]), line);

        Assert.Equal((ReconciliationGroup.Matched, MatchRule.Installment, notebook), (entry.Group, entry.Rule, entry.Item));
    }

    [Fact]
    public void Installment_WithAnotherAmount_MatchesOnlyWithASimilarDescription()
    {
        var notebook = Item("2026-09-15", 300m, "Notebook Loja", installment: 3, installments: 10);
        var similar = Line("2026-11-15", 301.50m, "LOJA NOTEBOOK 3/10");
        var other = Line("2026-11-15", 50m, "ACADEMIA 3/10");

        var entries = InvoiceMatcher.Match([other, similar], [notebook]);

        Assert.Equal((ReconciliationGroup.DifferentAmount, MatchRule.Installment), (Of(entries, similar).Group, Of(entries, similar).Rule));
        Assert.Equal(ReconciliationGroup.OnlyInBank, Of(entries, other).Group);
    }

    [Fact]
    public void AmountAndDate_PairsWithinThreeDays_TheClosestFirst_EachOnlyOnce()
    {
        var coffee1 = Item("2026-11-10", 12m, "Cafe");
        var coffee2 = Item("2026-11-14", 12m, "Cafe");
        var far = Item("2026-11-01", 80m, "Farmacia");
        var lineA = Line("2026-11-13", 12m, "CAFETERIA");
        var lineB = Line("2026-11-11", 12m, "CAFETERIA");
        var lineC = Line("2026-11-08", 80m, "DROGARIA");

        var entries = InvoiceMatcher.Match([lineA, lineB, lineC], [coffee1, coffee2, far]);

        Assert.Equal((coffee2, MatchRule.AmountAndDate), (Of(entries, lineA).Item, Of(entries, lineA).Rule));
        Assert.Equal(coffee1, Of(entries, lineB).Item);
        // 7 dias de diferenca: fora da janela.
        Assert.Equal(ReconciliationGroup.OnlyInBank, Of(entries, lineC).Group);
        Assert.Equal(-80m, Assert.Single(entries, e => e.Group == ReconciliationGroup.OnlyInApp).Explains);
    }

    [Fact]
    public void Refunds_AreCreditsOnBothSides()
    {
        var refund = Item("2026-11-05", -50m, "Estorno loja");
        var line = Line("2026-11-06", -50m, "ESTORNO LOJA");

        Assert.Equal(ReconciliationGroup.Matched, Of(InvoiceMatcher.Match([line], [refund]), line).Group);
    }

    [Theory]
    [InlineData("LOJA PARCELA 03/10", 3, 10)]
    [InlineData("LOJA PARC 3/10", 3, 10)]
    [InlineData("LOJA PARC.03/12", 3, 12)]
    [InlineData("LOJA 2/2", 2, 2)]
    [InlineData("LOJA PARCELA 4 DE 6", 4, 6)]
    public void InstallmentOf_ReadsTheUsualFormats(string description, int number, int of)
    {
        Assert.Equal((number, of), InvoiceMatcher.InstallmentOf(description));
    }

    [Theory]
    [InlineData("UBER 15/10")]   // data, nao parcela: 15 > 10
    [InlineData("LOJA 1/1")]     // parcela unica nao e parcelamento
    [InlineData("LOJA 2026/11")]
    [InlineData("MERCADO")]
    public void InstallmentOf_IgnoresWhatIsNotAnInstallment(string description)
    {
        Assert.Null(InvoiceMatcher.InstallmentOf(description));
    }

    [Fact]
    public void Similarity_CountsCommonWords_IgnoringCaseAccentsAndNumbers()
    {
        Assert.Equal(2, InvoiceMatcher.Similarity("CAFÉ & COWORKING 12", "cafe coworking ltda"));
        Assert.Equal(0, InvoiceMatcher.Similarity("UBER", "99 Taxi"));
    }
}
