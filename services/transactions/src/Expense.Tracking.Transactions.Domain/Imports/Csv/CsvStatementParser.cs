using Expense.Tracking.Transactions.Domain.Common;

namespace Expense.Tracking.Transactions.Domain.Imports.Csv;

/// <summary>
/// Transforma as linhas de um CSV em linhas de extrato ou fatura, pelo mapeamento confirmado
/// (IMP-3, ADR-0004). Linha com data ou valor invalido sai marcada como invalida, sem derrubar o
/// arquivo. Toda linha valida ganha a impressao digital de <see cref="StatementFingerprint"/>.
/// O CSV nao diz se e fatura: o tipo vem de quem envia (ADR-0004, decisao 5).
/// </summary>
public static class CsvStatementParser
{
    public static Result<ParsedStatement> Parse(
        CsvTable table, CsvColumnMapping mapping, StatementKind kind, string currency = ParsedStatement.DefaultCurrency)
    {
        var columns = new[] { mapping.DateColumn, mapping.DescriptionColumn, mapping.AmountColumn, mapping.DebitColumn, mapping.CreditColumn }
            .Where(c => c is not null)
            .Select(c => c!.Value)
            .ToList();

        if (columns.Any(c => c < 0) || columns.Count != columns.Distinct().Count() ||
            (mapping.AmountColumn is null && (mapping.DebitColumn is null || mapping.CreditColumn is null)) ||
            (mapping.AmountColumn is not null && (mapping.DebitColumn is not null || mapping.CreditColumn is not null)) ||
            mapping.DecimalSeparator is not (',' or '.'))
        {
            return Result.Failure<ParsedStatement>(ImportErrors.MappingInvalid);
        }

        var rows = mapping.HasHeader ? table.Rows.Skip(1) : table.Rows;
        var lines = rows.Select(row => ReadLine(row, mapping)).ToList();

        if (lines.Count == 0)
        {
            return Result.Failure<ParsedStatement>(ImportErrors.FormatInvalid("O CSV nao tem lancamentos."));
        }

        return new ParsedStatement(kind, currency, StatementFingerprint.Assign(lines));
    }

    private static StatementLine ReadLine(CsvRow row, CsvColumnMapping mapping)
    {
        var description = ImportText.NormalizeSpaces(row.Cell(mapping.DescriptionColumn));
        var number = row.LineNumber;

        if (description.Length == 0)
        {
            return StatementLine.Invalid(number, description, "Linha sem descricao.");
        }

        if (!CsvValues.TryParseDate(row.Cell(mapping.DateColumn), out var date))
        {
            return StatementLine.Invalid(number, description, "Data ausente ou invalida (use dd/MM/aaaa ou aaaa-MM-dd).");
        }

        decimal amount;
        if (mapping.AmountColumn is { } amountColumn)
        {
            if (!CsvValues.TryParseAmount(row.Cell(amountColumn), mapping.DecimalSeparator, out amount))
            {
                return StatementLine.Invalid(number, description, "Valor ausente ou invalido.");
            }

            if (mapping.InvertSign)
            {
                amount = -amount;
            }
        }
        else
        {
            // Celula vazia vale zero; o banco pode escrever o debito com ou sem sinal.
            if (!TryOptionalAmount(row.Cell(mapping.DebitColumn!.Value), mapping, out var debit) ||
                !TryOptionalAmount(row.Cell(mapping.CreditColumn!.Value), mapping, out var credit))
            {
                return StatementLine.Invalid(number, description, "Valor de debito ou credito invalido.");
            }

            if (debit != 0 && credit != 0)
            {
                return StatementLine.Invalid(number, description, "Linha com debito e credito ao mesmo tempo.");
            }

            amount = debit != 0 ? -Math.Abs(debit) : Math.Abs(credit);
        }

        if (amount == 0)
        {
            return StatementLine.Invalid(number, description, "Valor zerado ou ausente.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            return StatementLine.Invalid(number, description, "Valor com mais de 2 casas decimais.");
        }

        return StatementLine.Valid(number, date, amount, description, externalId: null);
    }

    private static bool TryOptionalAmount(string text, CsvColumnMapping mapping, out decimal amount)
    {
        amount = 0;
        return text.Length == 0 || CsvValues.TryParseAmount(text, mapping.DecimalSeparator, out amount);
    }
}
