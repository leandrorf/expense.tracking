using Expense.Tracking.Transactions.Domain.Common;

namespace Expense.Tracking.Transactions.Domain.Imports;

/// <summary>
/// Erros da leitura de arquivos. Codigos estaveis e descricoes que nunca ecoam o conteudo do
/// arquivo (extrato bancario e dado sensivel; ADR-0004, decisao 8).
/// </summary>
public static class ImportErrors
{
    public const string FormatInvalidCode = "import.format_invalid";

    public static Error FormatInvalid(string reason) => Error.Validation(FormatInvalidCode, reason);

    public static readonly Error MappingInvalid = Error.Validation(
        "import.mapping_invalid",
        "Mapeamento de colunas invalido: informe data, descricao e valor (ou debito e credito), cada um numa coluna diferente.");

    public static readonly Error MultipleStatements = Error.Validation(
        "import.multiple_statements",
        "O arquivo tem mais de uma conta ou cartao. Exporte um extrato ou fatura por vez.");
}
