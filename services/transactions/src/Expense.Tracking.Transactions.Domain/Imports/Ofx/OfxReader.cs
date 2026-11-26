using System.Globalization;
using Expense.Tracking.Transactions.Domain.Common;

namespace Expense.Tracking.Transactions.Domain.Imports.Ofx;

/// <summary>
/// Le extrato de conta (<c>BANKMSGSRSV1</c>/<c>STMTRS</c>) ou fatura de cartao
/// (<c>CREDITCARDMSGSRSV1</c>/<c>CCSTMTRS</c>) em OFX 1.x ou 2.x (IMP-2, ADR-0004). Sem I/O: recebe
/// os bytes ja em memoria. Cada <c>STMTTRN</c> vira uma <see cref="StatementLine"/> com data, valor
/// com sinal, descricao (MEMO, ou NAME sem MEMO) e FITID.
/// </summary>
public static class OfxReader
{
    public static Result<ParsedStatement> Read(ReadOnlySpan<byte> content)
    {
        var parsed = OfxDocument.Parse(content);
        if (parsed.IsFailure)
        {
            return Result.Failure<ParsedStatement>(parsed.Error);
        }

        var ofx = parsed.Value.Child("OFX");
        if (ofx is null)
        {
            return Result.Failure<ParsedStatement>(ImportErrors.FormatInvalid("O arquivo nao e um OFX."));
        }

        var statements = ofx.Descendants("STMTRS").Select(s => (Node: s, Kind: StatementKind.BankAccount))
            .Concat(ofx.Descendants("CCSTMTRS").Select(s => (Node: s, Kind: StatementKind.CreditCard)))
            .ToList();

        switch (statements.Count)
        {
            case 0:
                return Result.Failure<ParsedStatement>(ImportErrors.FormatInvalid(
                    "O OFX nao tem extrato de conta nem fatura de cartao."));
            case > 1:
                return Result.Failure<ParsedStatement>(ImportErrors.MultipleStatements);
        }

        var (statement, kind) = statements[0];

        var currency = statement.ChildValue("CURDEF")?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(currency))
        {
            currency = ParsedStatement.DefaultCurrency;
        }
        else if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))
        {
            return Result.Failure<ParsedStatement>(ImportErrors.FormatInvalid("A moeda do OFX e invalida."));
        }

        var transactionList = statement.Child("BANKTRANLIST");
        var transactions = transactionList?.Children.Where(c => c.Name == "STMTTRN") ?? [];
        var fitIdOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var lines = transactions.Select((trn, index) => ReadLine(trn, index + 1, fitIdOccurrences)).ToList();

        return new ParsedStatement(kind, currency, lines,
            kind == StatementKind.CreditCard ? CardTotals(statement, transactionList) : null);
    }

    /// <summary>
    /// RCN-3 (ADR-0011, decisao 2): total e periodo que a fatura declara. O saldo do cartao vem negativo na
    /// maioria dos bancos (o que se deve); o total e o valor sem sinal. Valor ou data ilegivel = ausente.
    /// </summary>
    private static StatementTotals? CardTotals(OfxNode statement, OfxNode? transactionList)
    {
        decimal? total = TryParseAmount(statement.Child("LEDGERBAL")?.ChildValue("BALAMT"), out var balance)
            && decimal.Round(balance, 2) == balance
                ? Math.Abs(balance)
                : null;
        DateOnly? start = TryParseDate(transactionList?.ChildValue("DTSTART"), out var from) ? from : null;
        DateOnly? end = TryParseDate(transactionList?.ChildValue("DTEND"), out var to) ? to : null;
        return total is null && start is null && end is null ? null : new StatementTotals(total, start, end);
    }

    private static StatementLine ReadLine(OfxNode trn, int number, Dictionary<string, int> fitIdOccurrences)
    {
        var description = ImportText.NormalizeSpaces(trn.ChildValue("MEMO")) is { Length: > 0 } memo
            ? memo
            : ImportText.NormalizeSpaces(trn.ChildValue("NAME"));

        if (description.Length == 0)
        {
            return StatementLine.Invalid(number, description, "Lancamento sem descricao (MEMO ou NAME).");
        }

        if (!TryParseDate(trn.ChildValue("DTPOSTED"), out var date))
        {
            return StatementLine.Invalid(number, description, "Data do lancamento (DTPOSTED) ausente ou invalida.");
        }

        if (!TryParseAmount(trn.ChildValue("TRNAMT"), out var amount))
        {
            return StatementLine.Invalid(number, description, "Valor do lancamento (TRNAMT) ausente ou invalido.");
        }

        if (amount == 0)
        {
            return StatementLine.Invalid(number, description, "Valor do lancamento zerado.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            return StatementLine.Invalid(number, description, "Valor do lancamento com mais de 2 casas decimais.");
        }

        return StatementLine.Valid(number, date, amount, description, ExternalId(trn.ChildValue("FITID"), fitIdOccurrences));
    }

    /// <summary>
    /// FITID e a chave de deduplicacao. Ha bancos que repetem o FITID dentro do mesmo arquivo; a
    /// k-esima repeticao vira "FITID#k" para cada lancamento continuar com chave propria e estavel
    /// entre reimportacoes do mesmo arquivo. Sem FITID, a chave fica para quem monta o lote.
    /// </summary>
    private static string? ExternalId(string? fitId, Dictionary<string, int> occurrences)
    {
        var trimmed = fitId?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var seen = occurrences.GetValueOrDefault(trimmed) + 1;
        occurrences[trimmed] = seen;
        return seen == 1 ? trimmed : $"{trimmed}#{seen}";
    }

    /// <summary>Datas OFX: <c>YYYYMMDD[HHMMSS[.XXX][[-3:BRT]]]</c>. Vale o dia como o banco escreveu.</summary>
    private static bool TryParseDate(string? value, out DateOnly date)
    {
        date = default;
        var trimmed = value?.Trim();
        return trimmed is { Length: >= 8 } &&
            DateOnly.TryParseExact(trimmed[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>
    /// Valor com sinal. O OFX pede ponto decimal, mas ha bancos brasileiros que usam virgula; com os
    /// dois, o ultimo separador e o decimal e o outro e de milhar.
    /// </summary>
    private static bool TryParseAmount(string? value, out decimal amount)
    {
        amount = 0;
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var lastDot = text.LastIndexOf('.');
        var lastComma = text.LastIndexOf(',');
        if (lastComma > lastDot)
        {
            text = text.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        }
        else if (lastComma >= 0)
        {
            text = text.Replace(",", string.Empty, StringComparison.Ordinal);
        }

        return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out amount);
    }
}
