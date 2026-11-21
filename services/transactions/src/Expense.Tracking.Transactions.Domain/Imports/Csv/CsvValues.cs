using System.Globalization;

namespace Expense.Tracking.Transactions.Domain.Imports.Csv;

/// <summary>Leitura de datas e valores como os bancos brasileiros escrevem no CSV.</summary>
internal static class CsvValues
{
    private static readonly string[] DateFormats = ["dd/MM/yyyy", "yyyy-MM-dd"];

    /// <summary>Datas <c>dd/MM/yyyy</c> ou <c>yyyy-MM-dd</c>, com ou sem hora depois de um espaco ou 'T'.</summary>
    public static bool TryParseDate(string value, out DateOnly date)
    {
        var text = value.Trim();
        var timeStart = text.IndexOfAny([' ', 'T']);
        if (timeStart > 0)
        {
            text = text[..timeStart];
        }

        return DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>
    /// Valor com sinal: aceita "R$", espacos, sinal de menos ou parenteses para negativo e
    /// separador de milhar (o que nao for o decimal informado).
    /// </summary>
    public static bool TryParseAmount(string value, char decimalSeparator, out decimal amount)
    {
        amount = 0;
        var text = value.Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        if (text.Length == 0)
        {
            return false;
        }

        var negative = false;
        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            negative = true;
            text = text[1..^1];
        }

        var thousands = decimalSeparator == ',' ? "." : ",";
        text = text.Replace(thousands, string.Empty, StringComparison.Ordinal);
        if (decimalSeparator == ',')
        {
            text = text.Replace(',', '.');
        }

        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out amount))
        {
            return false;
        }

        if (negative)
        {
            amount = -Math.Abs(amount);
        }

        return true;
    }

    /// <summary>
    /// Separador decimal mais provavel olhando as celulas de valor: virgula seguida de 1 ou 2
    /// digitos no fim indica virgula; ponto nessa posicao indica ponto. Sem pista, virgula (pt-BR).
    /// </summary>
    public static char GuessDecimalSeparator(IEnumerable<string> amountCells)
    {
        var commas = 0;
        var dots = 0;
        foreach (var cell in amountCells)
        {
            var text = cell.Trim().TrimEnd(')');
            var last = text.LastIndexOfAny([',', '.']);
            if (last < 0 || text.Length - last - 1 is < 1 or > 2)
            {
                continue;
            }

            if (text[last] == ',')
            {
                commas++;
            }
            else
            {
                dots++;
            }
        }

        return dots > commas ? '.' : ',';
    }
}
