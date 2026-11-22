using System.Text;
using Expense.Tracking.Transactions.Application.AccountingExport;
using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.AccountingExport;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.UnitTests.AccountingExport;

/// <summary>CTB-2 (ADR-0006, decisao 2): CSV que o Excel pt-BR abre direto, sem formula escondida.</summary>
public sealed class AccountingCsvTests
{
    [Theory]
    [InlineData(1234.56, "1234,56")]
    [InlineData(-1234.5, "-1234,50")]
    [InlineData(1000000, "1000000,00")]
    [InlineData(0.01, "0,01")]
    public void Amount_UsesDecimalComma_WithoutThousandsSeparator(decimal value, string expected) =>
        Assert.Equal(expected, AccountingCsv.Amount(value));

    [Fact]
    public void Date_IsDayMonthYear() => Assert.Equal("05/01/2026", AccountingCsv.Date(new DateOnly(2026, 1, 5)));

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "'=HYPERLINK(\"x\")")]
    [InlineData("+55 11", "'+55 11")]
    [InlineData("-SALDO", "'-SALDO")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tTAB", "'\tTAB")]
    [InlineData("Padaria", "Padaria")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Text_NeutralizesFormulas(string? value, string expected) => Assert.Equal(expected, AccountingCsv.Text(value));

    [Fact]
    public void Line_QuotesFieldsWithSeparatorQuotesOrLineBreaks_AndEndsInCrlf() =>
        Assert.Equal("a;\"b;c\";\"diz \"\"oi\"\"\";\"linha\nnova\"\r\n",
            AccountingCsv.Line(["a", "b;c", "diz \"oi\"", "linha\nnova"]));

    [Theory]
    [InlineData("12345678000195", "12.345.678/0001-95")]
    [InlineData("12345678909", "123.456.789-09")]
    [InlineData(null, "")]
    public void TaxId_IsPunctuated(string? digits, string expected) => Assert.Equal(expected, AccountingCsv.TaxId(digits));

    [Fact]
    public void ToBytes_StartsWithTheUtf8Bom_SoExcelReadsTheAccents()
    {
        var bytes = AccountingCsv.ToBytes("Descrição");

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("Descrição", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public void EntriesSheet_SignsExpensesNegative_AndKeepsTheInstallmentPurchaseWhole()
    {
        var csv = AccountingEntriesSheet.Write(
        [
            new AccountingEntry(new DateOnly(2026, 10, 3), "Notebook", "Equipamentos", null, TransactionType.Expense,
                3000m, "BRL", PaymentMethod.CreditCard, "Empresa final 1234", 3, "Conta PJ", "12345678000195", null, Imported: false),
            new AccountingEntry(new DateOnly(2026, 10, 10), "Cliente A; NF 12", "Serviços", null, TransactionType.Income,
                5000m, "BRL", PaymentMethod.Pix, null, 1, "Conta PJ", "12345678000195", "pago em dia", Imported: true),
        ]);

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
        [
            "Data;Descrição;Categoria;Conta contábil;Tipo;Valor;Moeda;Meio;Cartão;Parcelas;Conta;CNPJ;Observações;Origem",
            "03/10/2026;Notebook;Equipamentos;;Despesa;-3000,00;BRL;Cartão de crédito;Empresa final 1234;3;Conta PJ;12.345.678/0001-95;;digitado",
            "10/10/2026;\"Cliente A; NF 12\";Serviços;;Receita;5000,00;BRL;Pix;;1;Conta PJ;12.345.678/0001-95;pago em dia;importado",
        ], lines);
    }

    [Fact]
    public void FileName_CarriesTheCnpj_OnlyWhenAllAccountsShareIt()
    {
        var month = new DateOnly(2026, 10, 1);
        AccountInfo Account(string? taxId) => new(Guid.NewGuid(), "PJ", "Business", taxId, "BRL", true);

        Assert.Equal("contabil-12345678000195-2026-10.zip",
            ExportAccountingHandler.FileName([Account("12345678000195"), Account("12345678000195")], month));
        Assert.Equal("contabil-2026-10.zip",
            ExportAccountingHandler.FileName([Account("12345678000195"), Account("98765432000110")], month));
    }
}
