namespace Expense.Tracking.Transactions.Domain.Imports.Csv;

/// <summary>
/// Proposta de mapeamento para o usuario confirmar (IMP-8). <see cref="Mapping"/> e nulo quando
/// nao deu para descobrir data, descricao e valor: o usuario escolhe as colunas. As primeiras
/// linhas vao junto para a SPA mostrar o arquivo (so para quem o enviou; nunca para log).
/// </summary>
public sealed record CsvMappingProposal(
    CsvColumnMapping? Mapping, IReadOnlyList<string>? Headers, int ColumnCount, IReadOnlyList<CsvRow> Preview);

/// <summary>
/// Propoe o mapeamento de colunas de um CSV sem mapeamento informado (IMP-3): detecta cabecalho,
/// reconhece nomes de coluna comuns em bancos brasileiros e, sem nome conhecido, olha o conteudo
/// (coluna com datas, coluna com valores, coluna com o texto mais longo).
/// </summary>
public static class CsvMappingDetector
{
    public const int PreviewRows = 5;
    private const int SampleRows = 50;

    private static readonly string[] DateNames = ["data", "date", "dt"];
    private static readonly string[] DescriptionNames =
        ["descricao", "description", "historico", "lancamento", "estabelecimento", "title", "titulo", "memo", "detalhe"];
    private static readonly string[] AmountNames = ["valor", "amount", "value", "quantia"];
    private static readonly string[] DebitNames = ["debito", "debit", "saida"];
    private static readonly string[] CreditNames = ["credito", "credit", "entrada"];

    public static CsvMappingProposal Propose(CsvTable table)
    {
        var rows = table.Rows;
        var columnCount = rows.Count == 0 ? 0 : rows.Max(r => r.Cells.Count);
        if (rows.Count == 0)
        {
            return new CsvMappingProposal(null, null, 0, []);
        }

        var hasHeader = LooksLikeHeader(rows);
        var headers = hasHeader ? rows[0].Cells.Select(c => c.Trim()).ToList() : null;
        var data = (hasHeader ? rows.Skip(1) : rows).ToList();
        var sample = data.Take(SampleRows).ToList();
        var preview = data.Take(PreviewRows).ToList();

        var mapping = (headers is null ? null : ByHeaderNames(headers, hasHeader)) ?? ByContent(sample, columnCount, hasHeader);
        if (mapping is not null)
        {
            var amountCells = new[] { mapping.AmountColumn, mapping.DebitColumn, mapping.CreditColumn }
                .Where(c => c is not null)
                .SelectMany(c => sample.Select(r => r.Cell(c!.Value)));
            mapping = mapping with { DecimalSeparator = CsvValues.GuessDecimalSeparator(amountCells) };
        }

        return new CsvMappingProposal(mapping, headers, columnCount, preview);
    }

    /// <summary>Primeira linha sem data nem valor, seguida de uma linha que tem data.</summary>
    private static bool LooksLikeHeader(IReadOnlyList<CsvRow> rows)
    {
        var first = rows[0].Cells;
        if (first.Any(c => CsvValues.TryParseDate(c, out _) || CsvValues.TryParseAmount(c, ',', out _)))
        {
            return false;
        }

        return rows.Count == 1 || rows[1].Cells.Any(c => CsvValues.TryParseDate(c, out _));
    }

    private static CsvColumnMapping? ByHeaderNames(IReadOnlyList<string> headers, bool hasHeader)
    {
        var normalized = headers.Select(Fold).ToList();

        int? Find(string[] names, params int?[] taken)
        {
            for (var i = 0; i < normalized.Count; i++)
            {
                if (!taken.Contains(i) && names.Any(n => normalized[i] == n || normalized[i].StartsWith(n + " ", StringComparison.Ordinal)))
                {
                    return i;
                }
            }

            return null;
        }

        var date = Find(DateNames);
        var description = Find(DescriptionNames, date);
        if (date is null || description is null)
        {
            return null;
        }

        var amount = Find(AmountNames, date, description);
        if (amount is not null)
        {
            return new CsvColumnMapping(date.Value, description.Value, amount, null, null, hasHeader);
        }

        var debit = Find(DebitNames, date, description);
        var credit = Find(CreditNames, date, description, debit);
        return debit is not null && credit is not null
            ? new CsvColumnMapping(date.Value, description.Value, null, debit, credit, hasHeader)
            : null;
    }

    /// <summary>Sem nomes conhecidos: a coluna mais cheia de datas, a de valores e a de texto mais longo.</summary>
    private static CsvColumnMapping? ByContent(IReadOnlyList<CsvRow> sample, int columnCount, bool hasHeader)
    {
        if (sample.Count == 0)
        {
            return null;
        }

        double Share(int column, Func<string, bool> test) =>
            sample.Count(r => test(r.Cell(column))) / (double)sample.Count;

        var columns = Enumerable.Range(0, columnCount).ToList();
        var date = columns
            .Select(c => (Column: c, Share: Share(c, v => CsvValues.TryParseDate(v, out _))))
            .Where(x => x.Share >= 0.8)
            .OrderByDescending(x => x.Share)
            .Select(x => (int?)x.Column)
            .FirstOrDefault();
        if (date is null)
        {
            return null;
        }

        // Valor: a primeira coluna em que quase toda linha e numero (e nao data). O saldo, quando
        // existe, costuma vir depois do valor.
        var amount = columns
            .Where(c => c != date)
            .Where(c => Share(c, v => CsvValues.TryParseAmount(v, ',', out _) && !CsvValues.TryParseDate(v, out _)) >= 0.8)
            .Select(c => (int?)c)
            .FirstOrDefault();
        if (amount is null)
        {
            return null;
        }

        var description = columns
            .Where(c => c != date && c != amount)
            .Select(c => (Column: c, Length: sample.Average(r => r.Cell(c).Length)))
            .Where(x => x.Length > 0)
            .OrderByDescending(x => x.Length)
            .Select(x => (int?)x.Column)
            .FirstOrDefault();

        return description is null
            ? null
            : new CsvColumnMapping(date.Value, description.Value, amount, null, null, hasHeader);
    }

    /// <summary>Minusculas sem acento, para comparar nomes de coluna ("Descrição" = "descricao").</summary>
    private static string Fold(string value)
    {
        const string from = "áàâãäéèêëíìîïóòôõöúùûüç";
        const string to = "aaaaaeeeeiiiiooooouuuuc";
        var chars = value.Trim().ToLowerInvariant().Select(ch => from.IndexOf(ch) is var i and >= 0 ? to[i] : ch);
        return string.Join(' ', new string(chars.ToArray()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
