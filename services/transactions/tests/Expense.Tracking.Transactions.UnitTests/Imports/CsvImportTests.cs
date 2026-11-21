using System.Text;
using Expense.Tracking.Transactions.Domain.Imports;
using Expense.Tracking.Transactions.Domain.Imports.Csv;
using Expense.Tracking.Transactions.Infrastructure.Imports;

namespace Expense.Tracking.Transactions.UnitTests.Imports;

/// <summary>
/// IMP-3: CSV separado em celulas (adaptador CsvHelper), mapeamento proposto e linhas de extrato
/// ou fatura com impressao digital. Arquivos sinteticos em Imports/Samples.
/// </summary>
public sealed class CsvImportTests
{
    private static readonly CsvHelperTableReader Reader = new();

    private static byte[] Sample(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Imports", "Samples", name));

    private static CsvTable Table(byte[] content)
    {
        var result = Reader.Read(content);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Description : null);
        return result.Value;
    }

    private static CsvTable Table(string content) => Table(Encoding.UTF8.GetBytes(content));

    private static ParsedStatement Parse(CsvTable table, CsvColumnMapping mapping, StatementKind kind = StatementKind.BankAccount)
    {
        var result = CsvStatementParser.Parse(table, mapping, kind);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Description : null);
        return result.Value;
    }

    private static readonly CsvColumnMapping DateDescriptionAmount = new(0, 1, 2, null, null, HasHeader: false);

    [Fact]
    public void Read_SemicolonCommaDecimalWindows1252_WithExplicitMapping_ReadsSignedLines()
    {
        var table = Table(Sample("extrato-conta.csv"));
        var mapping = new CsvColumnMapping(0, 1, 2, null, null, HasHeader: true);

        var statement = Parse(table, mapping);

        Assert.Equal(StatementKind.BankAccount, statement.Kind);
        Assert.Equal("BRL", statement.Currency);
        Assert.Equal(6, statement.Lines.Count);

        var bakery = statement.Lines[1];
        Assert.True(bakery.IsValid);
        Assert.Equal(3, bakery.Number);
        Assert.Equal(new DateOnly(2026, 10, 2), bakery.Date);
        Assert.Equal(-18.50m, bakery.Amount);
        Assert.Equal("PADARIA SÃO JOÃO", bakery.Description);

        Assert.Equal(1250.00m, statement.Lines[3].Amount);
        Assert.Equal("PAG BOLETO ENERGIA; LUZ", statement.Lines[4].Description);
    }

    [Fact]
    public void Read_InvalidDateOrMissingAmount_MarksOnlyThatLineInvalid()
    {
        var statement = Parse(Table(Sample("extrato-conta.csv")), new CsvColumnMapping(0, 1, 2, null, null, HasHeader: true));

        var openingBalance = statement.Lines[0];
        Assert.False(openingBalance.IsValid);
        Assert.Equal(2, openingBalance.Number);
        Assert.Contains("Valor", openingBalance.Problem);

        var badDate = statement.Lines[5];
        Assert.False(badDate.IsValid);
        Assert.Equal(7, badDate.Number);
        Assert.Contains("Data", badDate.Problem);
        Assert.Null(badDate.ExternalId);

        Assert.Equal(4, statement.Lines.Count(l => l.IsValid));
    }

    [Fact]
    public void Fingerprint_TwoIdenticalExpensesOnTheSameDay_AreTwoLinesWithDistinctStableKeys()
    {
        var mapping = new CsvColumnMapping(0, 1, 2, null, null, HasHeader: true);
        var first = Parse(Table(Sample("extrato-conta.csv")), mapping).Lines;
        var again = Parse(Table(Sample("extrato-conta.csv")), mapping).Lines;

        var bakery1 = first[1].ExternalId!;
        var bakery2 = first[2].ExternalId!;
        Assert.StartsWith(StatementFingerprint.Prefix, bakery1);
        Assert.EndsWith("-1", bakery1);
        Assert.EndsWith("-2", bakery2);
        Assert.Equal(bakery1[..^2], bakery2[..^2]);

        // Reimportar o mesmo arquivo gera as mesmas chaves.
        Assert.Equal(first.Select(l => l.ExternalId), again.Select(l => l.ExternalId));
        Assert.Equal(4, first.Where(l => l.IsValid).Select(l => l.ExternalId).Distinct().Count());
    }

    [Fact]
    public void Fingerprint_IgnoresCaseAndSpacesButNotDateOrAmount()
    {
        var a = Parse(Table("02/10/2026;Uber *Trip;-32,40\n"), DateDescriptionAmount).Lines[0].ExternalId;
        var b = Parse(Table("02/10/2026;UBER   *TRIP ;-32,40\n"), DateDescriptionAmount).Lines[0].ExternalId;
        var otherDay = Parse(Table("03/10/2026;Uber *Trip;-32,40\n"), DateDescriptionAmount).Lines[0].ExternalId;
        var otherAmount = Parse(Table("02/10/2026;Uber *Trip;-32,41\n"), DateDescriptionAmount).Lines[0].ExternalId;

        Assert.Equal(a, b);
        Assert.NotEqual(a, otherDay);
        Assert.NotEqual(a, otherAmount);
    }

    [Fact]
    public void Fingerprint_KeepsFitIdWhenPresentAndFillsOnlyMissingKeys()
    {
        var lines = new[]
        {
            StatementLine.Valid(1, new DateOnly(2026, 10, 1), -10m, "A", "FIT-1"),
            StatementLine.Valid(2, new DateOnly(2026, 10, 1), -10m, "A", null),
            StatementLine.Invalid(3, "B", "Data invalida"),
        };

        var assigned = StatementFingerprint.Assign(lines);

        Assert.Equal("FIT-1", assigned[0].ExternalId);
        Assert.StartsWith(StatementFingerprint.Prefix, assigned[1].ExternalId);
        Assert.Null(assigned[2].ExternalId);
    }

    [Fact]
    public void Read_SeparateDebitAndCreditColumns_DebitIsNegativeCreditPositive()
    {
        var table = Table(Sample("extrato-debito-credito.csv"));
        var mapping = new CsvColumnMapping(0, 1, null, 2, 3, HasHeader: true);

        var lines = Parse(table, mapping).Lines;

        Assert.Equal(2, lines.Count);
        Assert.Equal(-29.90m, lines[0].Amount);
        Assert.Equal(3, lines[0].Number);
        Assert.Equal(3000.00m, lines[1].Amount);
    }

    [Theory]
    [InlineData("01/10/2026;X;10,00;5,00", "ao mesmo tempo")]
    [InlineData("01/10/2026;X;;", "zerado")]
    [InlineData("01/10/2026;X;abc;", "invalido")]
    public void Read_DebitCreditColumnsWithBadValues_MarksLineInvalid(string row, string reason)
    {
        var mapping = new CsvColumnMapping(0, 1, null, 2, 3, HasHeader: false);

        var line = Assert.Single(Parse(Table(row + "\n"), mapping).Lines);

        Assert.False(line.IsValid);
        Assert.Contains(reason, line.Problem);
    }

    [Theory]
    [InlineData("-1.234,56", ',', -1234.56)]
    [InlineData("R$ 1.234,56", ',', 1234.56)]
    [InlineData("(10,00)", ',', -10.00)]
    [InlineData("-1,234.56", '.', -1234.56)]
    [InlineData("89.90", '.', 89.90)]
    public void Read_AmountFormats_AreUnderstood(string amount, char decimalSeparator, decimal expected)
    {
        var mapping = DateDescriptionAmount with { DecimalSeparator = decimalSeparator };

        var line = Assert.Single(Parse(Table($"01/10/2026\t\"X\"\t\"{amount}\"\n"), mapping).Lines);

        Assert.Equal(expected, line.Amount);
    }

    [Theory]
    [InlineData("2026-10-01")]
    [InlineData("01/10/2026")]
    [InlineData("01/10/2026 13:45")]
    [InlineData("2026-10-01T13:45:00")]
    public void Read_DateFormats_AreUnderstood(string date)
    {
        var line = Assert.Single(Parse(Table($"{date};X;-1,00\n"), DateDescriptionAmount).Lines);

        Assert.Equal(new DateOnly(2026, 10, 1), line.Date);
    }

    [Fact]
    public void Read_CardStatementWithPositivePurchases_InvertSignMakesPurchasesDebits()
    {
        var table = Table(Sample("fatura-cartao.csv"));
        var mapping = new CsvColumnMapping(0, 1, 2, null, null, HasHeader: true, DecimalSeparator: '.', InvertSign: true);

        var statement = Parse(table, mapping, StatementKind.CreditCard);

        Assert.Equal(StatementKind.CreditCard, statement.Kind);
        Assert.Equal([-89.90m, -32.40m, 1250.00m, -450.00m], statement.Lines.Select(l => l.Amount!.Value));
        Assert.Equal("Café & Coworking, Ágil", statement.Lines[3].Description);
    }

    [Theory]
    [InlineData(0, 0, 2)]      // data e descricao na mesma coluna
    [InlineData(0, 1, null)]   // sem valor nem debito/credito
    [InlineData(-1, 1, 2)]
    public void Parse_InvalidMapping_IsRejected(int date, int description, int? amount)
    {
        var mapping = new CsvColumnMapping(date, description, amount, null, null, HasHeader: false);

        var result = CsvStatementParser.Parse(Table("01/10/2026;X;-1,00\n"), mapping, StatementKind.BankAccount);

        Assert.True(result.IsFailure);
        Assert.Equal("import.mapping_invalid", result.Error.Code);
    }

    [Fact]
    public void Parse_OnlyHeader_IsRejected()
    {
        var result = CsvStatementParser.Parse(Table("Data;Descricao;Valor\n"), DateDescriptionAmount with { HasHeader = true },
            StatementKind.BankAccount);

        Assert.True(result.IsFailure);
        Assert.Equal(ImportErrors.FormatInvalidCode, result.Error.Code);
    }

    [Fact]
    public void Propose_HeaderWithKnownNames_MapsColumnsAndGuessesCommaDecimal()
    {
        var proposal = CsvMappingDetector.Propose(Table(Sample("extrato-conta.csv")));

        Assert.Equal(new CsvColumnMapping(0, 1, 2, null, null, HasHeader: true, DecimalSeparator: ','), proposal.Mapping);
        Assert.Equal(["Data", "Descrição", "Valor", "Saldo"], proposal.Headers);
        Assert.Equal(4, proposal.ColumnCount);
        Assert.Equal(CsvMappingDetector.PreviewRows, proposal.Preview.Count);
        Assert.Equal(2, proposal.Preview[0].LineNumber);
    }

    [Fact]
    public void Propose_EnglishHeadersWithDotDecimal_MapsColumns()
    {
        var proposal = CsvMappingDetector.Propose(Table(Sample("fatura-cartao.csv")));

        Assert.Equal(new CsvColumnMapping(0, 1, 2, null, null, HasHeader: true, DecimalSeparator: '.'), proposal.Mapping);
    }

    [Fact]
    public void Propose_DebitAndCreditHeaders_MapsSeparateColumns()
    {
        var proposal = CsvMappingDetector.Propose(Table(Sample("extrato-debito-credito.csv")));

        Assert.Equal(new CsvColumnMapping(0, 1, null, 2, 3, HasHeader: true, DecimalSeparator: ','), proposal.Mapping);
    }

    [Fact]
    public void Propose_NoHeader_MapsByContent()
    {
        var proposal = CsvMappingDetector.Propose(Table(
            "-18,50;02/10/2026;PADARIA EXEMPLO\n-120,00;05/10/2026;MERCADO EXEMPLO LTDA\n5000,00;06/10/2026;SALARIO\n"));

        Assert.Null(proposal.Headers);
        Assert.Equal(new CsvColumnMapping(1, 2, 0, null, null, HasHeader: false, DecimalSeparator: ','), proposal.Mapping);
    }

    [Fact]
    public void Propose_UnknownHeadersAndNoDates_LeavesMappingToTheUser()
    {
        var proposal = CsvMappingDetector.Propose(Table("Coluna A;Coluna B\nfoo;bar\nbaz;qux\n"));

        Assert.Null(proposal.Mapping);
        Assert.Equal(2, proposal.ColumnCount);
    }

    [Fact]
    public void Read_CommaSeparatedWithQuotedCommas_DetectsComma()
    {
        var table = Table("date,title,amount\n2026-10-01,\"Loja, Centro\",-10.00\n");

        Assert.Equal(["2026-10-01", "Loja, Centro", "-10.00"], table.Rows[1].Cells);
    }

    [Fact]
    public void Read_QuotedFieldWithLineBreak_KeepsLineNumbersOfTheFile()
    {
        var table = Table("Data;Descricao;Valor\n01/10/2026;\"linha\nquebrada\";-1,00\n02/10/2026;X;-2,00\n");

        Assert.Equal([1, 2, 4], table.Rows.Select(r => r.LineNumber));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n  \n")]
    public void Read_EmptyFile_IsRejected(string content)
    {
        var result = Reader.Read(Encoding.UTF8.GetBytes(content));

        Assert.True(result.IsFailure);
        Assert.Equal(ImportErrors.FormatInvalidCode, result.Error.Code);
    }

    [Fact]
    public void Read_BinaryFile_IsRejected()
    {
        var result = Reader.Read([0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x53, 0x45, 0x47]);

        Assert.True(result.IsFailure);
        Assert.Equal(ImportErrors.FormatInvalidCode, result.Error.Code);
    }

    [Fact]
    public void Read_UnclosedQuote_IsRejectedWithoutEchoingContent()
    {
        var result = Reader.Read(Encoding.UTF8.GetBytes("01/10/2026;\"SEGREDO-NO-ARQUIVO;-1,00\n02/10/2026;X;-2,00\n"));

        Assert.True(result.IsFailure);
        Assert.Equal(ImportErrors.FormatInvalidCode, result.Error.Code);
        Assert.DoesNotContain("SEGREDO", result.Error.Description);
    }
}
