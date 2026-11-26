using Expense.Tracking.Transactions.Domain.Imports;

namespace Expense.Tracking.Transactions.UnitTests.Imports;

/// <summary>RCN-5 (ADR-0011, decisao 5): parcela da fatura do banco de uma compra ja lancada nao vira compra nova.</summary>
public sealed class InstallmentLinesTests
{
    private static readonly Guid Notebook = Guid.NewGuid();
    private static readonly InstallmentPurchase NotebookIn3 = new(Notebook, 100m, 3, new DateOnly(2026, 9, 15));

    [Theory]
    [InlineData(1, 33.34)]
    [InlineData(2, 33.33)]
    [InlineData(3, 33.33)]
    public void AmountOf_IsTheSameSplitAsTheInvoice(int number, double expected)
    {
        Assert.Equal((decimal)expected, InstallmentLines.AmountOf(100m, 3, number));
    }

    [Fact]
    public void Match_TheInstallmentAmountOfAPreviousPurchase_EachInstallmentOnce()
    {
        var matches = InstallmentLines.Match(
        [
            (1, new DateOnly(2026, 11, 12), 33.33m, "LOJA PARCELA 02/03"),
            (2, new DateOnly(2026, 11, 12), 33.33m, "LOJA PARCELA 02/03"), // repetida: a parcela 2 ja casou
            (3, new DateOnly(2026, 11, 12), 33.34m, "LOJA PARCELA 01/03"),
            (4, new DateOnly(2026, 11, 12), 40.00m, "LOJA PARCELA 03/03"), // outro valor
            (5, new DateOnly(2026, 11, 12), 33.33m, "LOJA 3/4"),          // outro numero de parcelas
        ], [NotebookIn3]);

        Assert.Equal(new Dictionary<int, Guid> { [1] = Notebook, [3] = Notebook }, matches);
    }

    [Fact]
    public void Match_IgnoresPurchasesAfterTheLineOrTooOld()
    {
        Assert.Empty(InstallmentLines.Match([(1, new DateOnly(2026, 9, 1), 33.33m, "LOJA 2/3")], [NotebookIn3]));
        Assert.Empty(InstallmentLines.Match([(1, new DateOnly(2027, 6, 1), 33.33m, "LOJA 2/3")], [NotebookIn3]));
    }
}
