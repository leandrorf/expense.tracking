namespace Expense.Tracking.Transactions.Domain.Imports.Csv;

/// <summary>
/// CSV ja separado em celulas (o adaptador que le os bytes fica na Infrastructure). Linhas em
/// branco ja foram descartadas; <see cref="CsvRow.LineNumber"/> e a linha no arquivo, para o
/// usuario achar a linha com problema.
/// </summary>
public sealed record CsvTable(IReadOnlyList<CsvRow> Rows);

public sealed record CsvRow(int LineNumber, IReadOnlyList<string> Cells)
{
    public string Cell(int column) => column < Cells.Count ? Cells[column].Trim() : string.Empty;
}

/// <summary>
/// Quais colunas (indice a partir de 0) sao data, descricao e valor (ou debito e credito
/// separados), e como ler os numeros (ADR-0004, decisao 7). Proposto pelo
/// <see cref="CsvMappingDetector"/> e confirmado pelo usuario na SPA (IMP-8).
/// </summary>
/// <param name="HasHeader">A primeira linha e cabecalho e nao vira lancamento.</param>
/// <param name="DecimalSeparator">',' ou '.'; o outro separador e tratado como de milhar.</param>
/// <param name="InvertSign">
/// Para arquivos que trazem a compra positiva e o pagamento negativo (comum em fatura de cartao):
/// o sinal de <see cref="AmountColumn"/> e invertido para debito continuar negativo.
/// </param>
public sealed record CsvColumnMapping(
    int DateColumn,
    int DescriptionColumn,
    int? AmountColumn,
    int? DebitColumn,
    int? CreditColumn,
    bool HasHeader,
    char DecimalSeparator = ',',
    bool InvertSign = false)
{
    public bool UsesDebitCredit => AmountColumn is null;

    /// <summary>
    /// Colunas nao negativas e distintas; valor numa coluna so ou debito e credito separados (nunca
    /// os dois); separador decimal ',' ou '.'.
    /// </summary>
    public bool IsValid
    {
        get
        {
            var columns = new[] { DateColumn, DescriptionColumn, AmountColumn, DebitColumn, CreditColumn }
                .Where(c => c is not null)
                .Select(c => c!.Value)
                .ToList();

            return columns.All(c => c >= 0) && columns.Count == columns.Distinct().Count() &&
                (AmountColumn is null ? DebitColumn is not null && CreditColumn is not null
                                      : DebitColumn is null && CreditColumn is null) &&
                DecimalSeparator is ',' or '.';
        }
    }
}
