using System.Text;
using Expense.Tracking.Transactions.Domain.Imports;
using Expense.Tracking.Transactions.Domain.Imports.Ofx;

namespace Expense.Tracking.Transactions.UnitTests.Imports;

/// <summary>IMP-2: leitura de extrato e fatura em OFX, com arquivos sinteticos (Imports/Samples).</summary>
public sealed class OfxReaderTests
{
    private static byte[] Sample(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Imports", "Samples", name));

    private static byte[] Utf8(string content) => Encoding.UTF8.GetBytes(content);

    private const string SgmlHeader = "OFXHEADER:100\r\nDATA:OFXSGML\r\nVERSION:102\r\nCHARSET:1252\r\n\r\n";

    private static string SgmlBank(string transactions) =>
        SgmlHeader + "<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><CURDEF>BRL<BANKTRANLIST>" +
        transactions + "</BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>";

    [Fact]
    public void Read_Ofx1BankStatementInWindows1252_ReadsEveryTransaction()
    {
        var result = OfxReader.Read(Sample("extrato-conta-ofx1.ofx"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Description : null);
        var statement = result.Value;
        Assert.Equal(StatementKind.BankAccount, statement.Kind);
        Assert.Equal("BRL", statement.Currency);
        Assert.Equal(5, statement.Lines.Count);
        Assert.All(statement.Lines, line => Assert.True(line.IsValid));

        var bakery = statement.Lines[0];
        Assert.Equal(1, bakery.Number);
        Assert.Equal(new DateOnly(2026, 10, 2), bakery.Date);
        Assert.Equal(-18.50m, bakery.Amount);
        Assert.Equal("PADARIA SÃO JOÃO", bakery.Description);
        Assert.Equal("202610020001", bakery.ExternalId);
    }

    [Fact]
    public void Read_Ofx1BankStatement_KeepsSignNormalizesSpacesAndAcceptsCommaDecimalAndName()
    {
        var lines = OfxReader.Read(Sample("extrato-conta-ofx1.ofx")).Value.Lines;

        Assert.Equal("PIX ENVIADO MERCADO EXEMPLO", lines[1].Description);
        Assert.Equal(5000.00m, lines[2].Amount);
        Assert.Equal("SALÁRIO EMPRESA FICTÍCIA", lines[2].Description);
        Assert.Equal(-245.90m, lines[3].Amount);
        Assert.Equal("PAG BOLETO ENERGIA & LUZ", lines[3].Description);
    }

    [Fact]
    public void Read_FitIdRepeatedInTheSameFile_GetsADistinctStableKey()
    {
        var lines = OfxReader.Read(Sample("extrato-conta-ofx1.ofx")).Value.Lines;

        Assert.Equal("202610020001", lines[0].ExternalId);
        Assert.Equal("202610020001#2", lines[4].ExternalId);
        Assert.Equal(lines.Count, lines.Select(l => l.ExternalId).Distinct().Count());
    }

    [Fact]
    public void Read_Ofx2CreditCardStatement_IsRecognizedAsCardWithPurchasesAsDebitAndPaymentAsCredit()
    {
        var result = OfxReader.Read(Sample("fatura-cartao-ofx2.ofx"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Description : null);
        var statement = result.Value;
        Assert.Equal(StatementKind.CreditCard, statement.Kind);
        Assert.Equal(4, statement.Lines.Count);
        Assert.All(statement.Lines.Where(l => l.ExternalId != "CC-0003"), l => Assert.True(l.Amount < 0));

        var payment = Assert.Single(statement.Lines, l => l.Amount > 0);
        Assert.Equal("PAGAMENTO DE FATURA", payment.Description);
        Assert.Equal(1250.00m, payment.Amount);
        Assert.Equal(new DateOnly(2026, 10, 20), payment.Date);

        Assert.Equal("Uber *Trip Help.Uber.com", statement.Lines[1].Description);
        Assert.Equal("CAFÉ & COWORKING ÁGIL", statement.Lines[3].Description);
    }

    [Fact]
    public void Read_Ofx1WithUtf8Bom_DecodesAsUtf8()
    {
        var content = Encoding.UTF8.GetPreamble()
            .Concat(Utf8(SgmlBank("<STMTTRN><DTPOSTED>20261001<TRNAMT>-10.00<FITID>1<MEMO>AÇAÍ</STMTTRN>")))
            .ToArray();

        var line = Assert.Single(OfxReader.Read(content).Value.Lines);

        Assert.Equal("AÇAÍ", line.Description);
    }

    [Fact]
    public void Read_Ofx1WithClosedLeafTags_IsAccepted()
    {
        var content = SgmlBank("<STMTTRN><DTPOSTED>20261001</DTPOSTED><TRNAMT>-10.00</TRNAMT><FITID>1</FITID><MEMO>X</MEMO></STMTTRN>");

        var line = Assert.Single(OfxReader.Read(Utf8(content)).Value.Lines);

        Assert.Equal(-10.00m, line.Amount);
        Assert.Equal("1", line.ExternalId);
    }

    [Fact]
    public void Read_StatementWithoutCurrency_DefaultsToBrl()
    {
        var content = SgmlHeader + "<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><BANKTRANLIST></BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>";

        var result = OfxReader.Read(Utf8(content));

        Assert.Equal("BRL", result.Value.Currency);
        Assert.Empty(result.Value.Lines);
    }

    [Theory]
    [InlineData("<DTPOSTED>2026XX01<TRNAMT>-10.00<FITID>1<MEMO>X", "DTPOSTED")]
    [InlineData("<TRNAMT>-10.00<FITID>1<MEMO>X", "DTPOSTED")]
    [InlineData("<DTPOSTED>20261001<TRNAMT>dez<FITID>1<MEMO>X", "TRNAMT")]
    [InlineData("<DTPOSTED>20261001<TRNAMT>0.00<FITID>1<MEMO>X", "zerado")]
    [InlineData("<DTPOSTED>20261001<TRNAMT>-10.005<FITID>1<MEMO>X", "2 casas")]
    [InlineData("<DTPOSTED>20261001<TRNAMT>-10.00<FITID>1", "sem descricao")]
    public void Read_InvalidTransaction_IsMarkedInvalidWithoutFailingTheFile(string transaction, string reason)
    {
        var content = SgmlBank(
            "<STMTTRN><DTPOSTED>20261001<TRNAMT>-5.00<FITID>ok<MEMO>VALIDA</STMTTRN>" +
            $"<STMTTRN>{transaction}</STMTTRN>");

        var result = OfxReader.Read(Utf8(content));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Lines[0].IsValid);
        var invalid = result.Value.Lines[1];
        Assert.False(invalid.IsValid);
        Assert.Equal(2, invalid.Number);
        Assert.Contains(reason, invalid.Problem);
        Assert.Null(invalid.Amount);
        Assert.Null(invalid.ExternalId);
    }

    [Fact]
    public void Read_Ofx2WithDoctypeAndExternalEntity_IsRejectedWithoutResolvingIt()
    {
        const string xxe = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE OFX [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><BANKTRANLIST>
            <STMTTRN><DTPOSTED>20261001</DTPOSTED><TRNAMT>-1.00</TRNAMT><FITID>1</FITID><MEMO>&xxe;</MEMO></STMTTRN>
            </BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>
            """;

        var result = OfxReader.Read(Utf8(xxe));

        Assert.True(result.IsFailure);
        Assert.Equal(ImportErrors.FormatInvalidCode, result.Error.Code);
        Assert.DoesNotContain("passwd", result.Error.Description);
    }

    [Fact]
    public void Read_Ofx2WithUndeclaredEntity_IsRejected()
    {
        const string content = """
            <?xml version="1.0" encoding="UTF-8"?>
            <OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><BANKTRANLIST>
            <STMTTRN><DTPOSTED>20261001</DTPOSTED><TRNAMT>-1.00</TRNAMT><FITID>1</FITID><MEMO>&segredo;</MEMO></STMTTRN>
            </BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>
            """;

        var result = OfxReader.Read(Utf8(content));

        Assert.True(result.IsFailure);
        Assert.Equal(ImportErrors.FormatInvalidCode, result.Error.Code);
        Assert.DoesNotContain("segredo", result.Error.Description);
    }

    [Fact]
    public void Read_Ofx1WithDoctype_IsRejected()
    {
        var content = SgmlHeader + "<!DOCTYPE OFX SYSTEM \"file:///etc/passwd\"><OFX></OFX>";

        var result = OfxReader.Read(Utf8(content));

        Assert.True(result.IsFailure);
        Assert.Equal(ImportErrors.FormatInvalidCode, result.Error.Code);
    }

    [Theory]
    [InlineData("SEGREDO-NO-ARQUIVO</BANKTRANLIST></STMTRS>")]          // sem </OFX> e agregados abertos
    [InlineData("<STMTTRN><DTPOSTED>20261001<TRNAMT>-1.00<MEMO>SEGREDO-NO-ARQUIVO")]
    [InlineData("<STMTTRN><MEMO>SEGREDO-NO-ARQUIVO</STMTTRN><STMTTRN")]  // tag cortada no meio
    public void Read_TruncatedOfx1_IsRejectedWithoutEchoingContent(string tail)
    {
        var content = SgmlHeader + "<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><BANKTRANLIST>" + tail;

        var result = OfxReader.Read(Utf8(content));

        Assert.True(result.IsFailure);
        Assert.Equal(ImportErrors.FormatInvalidCode, result.Error.Code);
        Assert.DoesNotContain("SEGREDO", result.Error.Description);
    }

    [Fact]
    public void Read_TruncatedOfx2_IsRejectedWithoutEchoingContent()
    {
        var full = Encoding.UTF8.GetString(Sample("fatura-cartao-ofx2.ofx"));
        var truncated = full[..full.IndexOf("COWORKING", StringComparison.Ordinal)];

        var result = OfxReader.Read(Utf8(truncated));

        Assert.True(result.IsFailure);
        Assert.Equal(ImportErrors.FormatInvalidCode, result.Error.Code);
        Assert.DoesNotContain("CAF", result.Error.Description);
    }

    [Theory]
    [InlineData("Data;Descricao;Valor\n01/10/2026;SEGREDO;-10,00\n")]
    [InlineData("")]
    [InlineData("{\"OFX\": true}")]
    [InlineData("<?xml version=\"1.0\"?><html><body>SEGREDO</body></html>")]
    public void Read_FileThatIsNotOfx_IsRejected(string content)
    {
        var result = OfxReader.Read(Utf8(content));

        Assert.True(result.IsFailure);
        Assert.Equal(ImportErrors.FormatInvalidCode, result.Error.Code);
        Assert.DoesNotContain("SEGREDO", result.Error.Description);
    }

    [Fact]
    public void Read_OfxWithoutStatement_IsRejected()
    {
        var result = OfxReader.Read(Utf8(SgmlHeader + "<OFX><SIGNONMSGSRSV1><SONRS><CODE>0</SONRS></SIGNONMSGSRSV1></OFX>"));

        Assert.True(result.IsFailure);
        Assert.Equal(ImportErrors.FormatInvalidCode, result.Error.Code);
    }

    [Fact]
    public void Read_OfxWithTwoStatements_IsRejected()
    {
        var statement = "<STMTTRNRS><STMTRS><CURDEF>BRL<BANKTRANLIST></BANKTRANLIST></STMTRS></STMTTRNRS>";
        var content = SgmlHeader + $"<OFX><BANKMSGSRSV1>{statement}{statement}</BANKMSGSRSV1></OFX>";

        var result = OfxReader.Read(Utf8(content));

        Assert.True(result.IsFailure);
        Assert.Equal("import.multiple_statements", result.Error.Code);
    }

    [Fact]
    public void Read_InvalidCurrency_IsRejected()
    {
        var content = SgmlHeader + "<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><CURDEF>REAL<BANKTRANLIST></BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>";

        var result = OfxReader.Read(Utf8(content));

        Assert.True(result.IsFailure);
        Assert.Equal(ImportErrors.FormatInvalidCode, result.Error.Code);
    }
}
