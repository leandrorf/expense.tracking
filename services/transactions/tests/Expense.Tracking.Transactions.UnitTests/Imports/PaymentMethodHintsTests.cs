using Expense.Tracking.Transactions.Domain.Imports;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.UnitTests.Imports;

/// <summary>IMP-6: pistas no memo do extrato sugerem o meio (palavra inteira, a primeira vence).</summary>
public sealed class PaymentMethodHintsTests
{
    [Theory]
    [InlineData("PIX ENVIADO MERCADO", PaymentMethod.Pix)]
    [InlineData("Transf Pix - Fulano", PaymentMethod.Pix)]
    [InlineData("PAG BOLETO ENERGIA", PaymentMethod.Boleto)]
    [InlineData("TED 001.0001 JOAO", PaymentMethod.BankTransfer)]
    [InlineData("SAQUE 24H", PaymentMethod.Cash)]
    [InlineData("PIX PAG BOLETO", PaymentMethod.Pix)]
    [InlineData("BOLETO/PIX", PaymentMethod.Boleto)]
    public void KnownWord_SuggestsTheMethod(string memo, PaymentMethod expected) =>
        Assert.Equal(expected, PaymentMethodHints.Suggest(memo));

    [Theory]
    [InlineData("PIXEL STORE")]
    [InlineData("TEDDY BEAR")]
    [InlineData("PADARIA")]
    [InlineData("")]
    [InlineData(null)]
    public void NoWholeWord_SuggestsNothing(string? memo) =>
        Assert.Null(PaymentMethodHints.Suggest(memo));
}
