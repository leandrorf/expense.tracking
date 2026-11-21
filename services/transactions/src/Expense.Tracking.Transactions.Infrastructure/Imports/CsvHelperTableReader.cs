using System.Text;
using CsvHelper;
using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Imports;
using Expense.Tracking.Transactions.Domain.Imports.Csv;
using CsvRow = Expense.Tracking.Transactions.Domain.Imports.Csv.CsvRow;

namespace Expense.Tracking.Transactions.Infrastructure.Imports;

/// <summary>
/// Adaptador de <see cref="ICsvTableReader"/> com CsvHelper (IMP-3, ADR-0004 decisao 7).
/// Codificacao: UTF-8 (com ou sem BOM) ou, se os bytes nao forem UTF-8 valido, Windows-1252.
/// Separador: a linha "sep=;" do Excel ou a deteccao do CsvHelper (',', ';', tab, '|').
/// Aspas malformadas recusam o arquivo; a mensagem da excecao nunca e repassada (cita o arquivo).
/// </summary>
public sealed class CsvHelperTableReader : ICsvTableReader
{
    private static readonly CsvReaderOptions Options = CsvReaderOptions.Default with
    {
        HasHeader = false,
        TrackLineNumbers = true,
        MalformedData = CsvMalformedDataHandling.Throw,
        IncludeValuesInExceptions = false,
        DetectSeparatorHint = true,
    };

    public Result<CsvTable> Read(ReadOnlySpan<byte> content)
    {
        if (content.IndexOf((byte)0) >= 0)
        {
            return Result.Failure<CsvTable>(ImportErrors.FormatInvalid("O arquivo nao e um CSV."));
        }

        var rows = new List<CsvRow>();
        try
        {
            using var reader = CsvReader.FromText(Decode(content), Options);
            while (reader.Read())
            {
                // Antes de copiar o registro: um arquivo de 5 MB pode ter milhoes de linhas curtas
                // ou de colunas, e nada disso deve ir para a memoria (IMP-4).
                if (reader.Current.FieldCount > ICsvTableReader.MaxColumns)
                {
                    return Result.Failure<CsvTable>(ImportErrors.FormatInvalid(
                        $"O CSV tem mais de {ICsvTableReader.MaxColumns} colunas; confira o separador."));
                }

                var cells = reader.Current.ToArray();
                if (cells.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                if (rows.Count == ICsvTableReader.MaxRows)
                {
                    return Result.Failure<CsvTable>(ImportErrors.TooManyLines);
                }

                rows.Add(new CsvRow((int)reader.LineNumber, cells));
            }
        }
        catch (CsvFormatException)
        {
            return Result.Failure<CsvTable>(ImportErrors.FormatInvalid(
                "O CSV esta malformado (aspas sem fechamento ou campo quebrado)."));
        }

        return rows.Count == 0
            ? Result.Failure<CsvTable>(ImportErrors.FormatInvalid("O CSV esta vazio."))
            : new CsvTable(rows);
    }

    private static string Decode(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(Encoding.UTF8.Preamble))
        {
            return Encoding.UTF8.GetString(content[Encoding.UTF8.Preamble.Length..]);
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            // Bancos brasileiros ainda exportam em Windows-1252.
            return CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(content);
        }
    }
}
