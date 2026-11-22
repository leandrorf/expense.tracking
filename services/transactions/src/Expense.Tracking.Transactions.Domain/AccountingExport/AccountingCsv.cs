using System.Globalization;
using System.Text;

namespace Expense.Tracking.Transactions.Domain.AccountingExport;

/// <summary>
/// CTB-2 (ADR-0006, decisao 2): CSV no padrao do Excel pt-BR, para o contador abrir sem configurar
/// nada: <c>;</c> como separador, virgula decimal sem separador de milhar, data <c>dd/MM/aaaa</c>,
/// UTF-8 com BOM e CRLF. Formatado a mao porque o servico roda com InvariantGlobalization.
/// </summary>
public static class AccountingCsv
{
    public const char Separator = ';';

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>Linha com os campos ja formatados, terminada em CRLF.</summary>
    public static string Line(IEnumerable<string> fields) =>
        string.Join(Separator, fields.Select(Quote)) + "\r\n";

    /// <summary>
    /// Texto livre (descricao, categoria, observacoes, nomes). O que comeca com <c>=</c>, <c>+</c>,
    /// <c>-</c>, <c>@</c>, tabulacao ou retorno de carro ganha um <c>'</c> na frente, para o Excel nao
    /// executar formula (CSV injection, CWE-1236).
    /// </summary>
    public static string Text(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty
        : value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value
        : value;

    /// <summary><c>-1234,56</c>: o Excel pt-BR e os sistemas contabeis leem como numero.</summary>
    public static string Amount(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    public static string Date(DateOnly value) =>
        value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>CNPJ (14 digitos) ou CPF (11) com a pontuacao; outro tamanho sai como veio.</summary>
    public static string TaxId(string? digits) => digits?.Length switch
    {
        14 => $"{digits[..2]}.{digits[2..5]}.{digits[5..8]}/{digits[8..12]}-{digits[12..]}",
        11 => $"{digits[..3]}.{digits[3..6]}.{digits[6..9]}-{digits[9..]}",
        _ => digits ?? string.Empty,
    };

    public static byte[] ToBytes(string content) =>
        [.. Utf8WithBom.GetPreamble(), .. Utf8WithBom.GetBytes(content)];

    /// <summary>Campo com <c>;</c>, aspas ou quebra de linha vai entre aspas, com as aspas dobradas (RFC 4180).</summary>
    private static string Quote(string field) =>
        field.AsSpan().IndexOfAny(";\"\r\n") >= 0 ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
}
