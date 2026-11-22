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

    public static readonly Error MappingNameRequired = Error.Validation(
        "import.mapping_name_required", "Informe um nome para o mapeamento (ex.: Banco X - fatura).");

    public static readonly Error MappingNameTooLong = Error.Validation(
        "import.mapping_name_too_long", "O nome do mapeamento deve ter no maximo 60 caracteres.");

    public static readonly Error MappingHeadersInvalid = Error.Validation(
        "import.mapping_headers_invalid", "Cabecalho invalido: ate 50 colunas, cada uma com ate 100 caracteres.");

    public static readonly Error MappingsLimit = Error.Conflict(
        "import.mappings_limit", "Limite de 50 mapeamentos salvos atingido. Exclua algum para salvar outro.");

    public static readonly Error MappingNotFound = Error.NotFound(
        "import.mapping_not_found", "Mapeamento nao encontrado.");

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

    public static readonly Error NotPending = Error.Conflict(
        "import.not_pending", "A importacao nao esta pendente.");

    public static readonly Error NotConfirmed = Error.Conflict(
        "import.not_confirmed", "So uma importacao confirmada pode ser desfeita.");

    public static readonly Error LineNotFound = Error.Validation(
        "import.line_not_found", "Linha nao pertence a esta importacao.");

    public static Error LineNotIncludable(int number) => Error.Validation(
        "import.line_not_includable", $"A linha {number} e duplicada ou invalida e nao pode ser incluida.");

    public static Error LinePaymentMethodInvalid(int number) => Error.Validation(
        "import.payment_method_invalid",
        $"Meio de pagamento invalido na linha {number}: fatura usa cartao de credito; extrato nao aceita cartao.");

    /// <summary>Linhas incluidas sem categoria; os numeros vao em <c>Metadata["lines"]</c> para a SPA destacar.</summary>
    public static Error CategoriesRequired(IReadOnlyList<int> lines) => Error.Validation(
        "import.categories_required",
        $"Toda linha incluida precisa de categoria. Faltam {lines.Count}.")
        with { Metadata = new Dictionary<string, object?> { ["lines"] = lines } };

    /// <summary>INV-11: linha incluida como pagamento de fatura sem cartao ou sem mes; numeros em <c>Metadata["lines"]</c>.</summary>
    public static Error InvoicesRequired(IReadOnlyList<int> lines) => Error.Validation(
        "import.invoice_required",
        $"Todo pagamento de fatura precisa do cartao e do mes da fatura. Faltam {lines.Count}.")
        with { Metadata = new Dictionary<string, object?> { ["lines"] = lines } };

    /// <summary>INV-11: pagamento de fatura so em saida de extrato de conta.</summary>
    public static Error LineInvoicePaymentInvalid(int number) => Error.Validation(
        "import.invoice_payment_invalid",
        $"A linha {number} nao pode ser pagamento de fatura: so saidas de extrato de conta.");

    public static readonly Error MultipleStatements = Error.Validation(
        "import.multiple_statements",
        "O arquivo tem mais de uma conta ou cartao. Exporte um extrato ou fatura por vez.");
}
