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

    public static readonly Error FileTooLarge = Error.Validation(
        "import.file_too_large", "O arquivo passa do limite de 5 MB.");

    public static readonly Error TooManyLines = Error.Validation(
        "import.too_many_lines", "O arquivo passa do limite de 5.000 lancamentos. Divida o periodo em mais de um arquivo.");

    public static readonly Error FormatNotAccepted = Error.Validation(
        FormatInvalidCode, "Formato nao aceito: envie um arquivo .ofx, .qfx ou .csv.");

    public static readonly Error FileRequired = Error.Validation(
        "import.file_required", "Envie o arquivo do extrato ou da fatura.");

    public static readonly Error MappingRequired = Error.Validation(
        "import.mapping_required", "Informe o mapeamento de colunas do CSV (veja POST /imports/csv/proposal).");

    public static readonly Error CardRequired = Error.Validation(
        "import.card_required", "Fatura de cartao: informe o cartao (creditCardId).");

    public static readonly Error CardNotAllowed = Error.Validation(
        "import.card_not_allowed", "O arquivo e extrato de conta: nao informe cartao.");

    public static readonly Error PaymentMethodRequired = Error.Validation(
        "import.payment_method_required", "Extrato de conta: informe o meio de pagamento padrao do lote (paymentMethod).");

    public static readonly Error CreditCardOnBankStatement = Error.Validation(
        "import.payment_method_invalid", "Extrato de conta nao aceita cartao de credito como meio de pagamento.");

    public static readonly Error BatchNotFound = Error.NotFound(
        "import.not_found", "Importacao nao encontrada.");

    public static readonly Error MultipleStatements = Error.Validation(
        "import.multiple_statements",
        "O arquivo tem mais de uma conta ou cartao. Exporte um extrato ou fatura por vez.");
}
