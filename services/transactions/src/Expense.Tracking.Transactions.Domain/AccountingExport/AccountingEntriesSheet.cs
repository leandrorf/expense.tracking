using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Domain.AccountingExport;

/// <summary>
/// Um lancamento como sai para o contador. <paramref name="Card"/> e o apelido com o final do
/// cartao; <paramref name="LedgerAccount"/> fica vazio ate o plano de contas (CTB-5).
/// </summary>
public sealed record AccountingEntry(
    DateOnly Date,
    string Description,
    string Category,
    string? LedgerAccount,
    TransactionType Type,
    decimal Amount,
    string Currency,
    PaymentMethod PaymentMethod,
    string? Card,
    int Installments,
    string AccountName,
    string? TaxId,
    string? Notes,
    bool Imported);

/// <summary>
/// CTB-2 (ADR-0006, decisao 3): <c>lancamentos.csv</c>, um lancamento por linha. A saida vai com
/// valor negativo; a compra parcelada sai uma vez, com o valor inteiro e o numero de parcelas.
/// </summary>
public static class AccountingEntriesSheet
{
    public const string FileName = "lancamentos.csv";

    private static readonly string[] Header =
    [
        "Data", "Descrição", "Categoria", "Conta contábil", "Tipo", "Valor", "Moeda", "Meio", "Cartão",
        "Parcelas", "Conta", "CNPJ", "Observações", "Origem",
    ];

    public static string Write(IEnumerable<AccountingEntry> entries)
    {
        var csv = new System.Text.StringBuilder(AccountingCsv.Line(Header));
        foreach (var e in entries)
        {
            csv.Append(AccountingCsv.Line(
            [
                AccountingCsv.Date(e.Date),
                AccountingCsv.Text(e.Description),
                AccountingCsv.Text(e.Category),
                AccountingCsv.Text(e.LedgerAccount),
                e.Type == TransactionType.Income ? "Receita" : "Despesa",
                AccountingCsv.Amount(e.Type == TransactionType.Income ? e.Amount : -e.Amount),
                e.Currency,
                MethodName(e.PaymentMethod),
                AccountingCsv.Text(e.Card),
                e.Installments.ToString(System.Globalization.CultureInfo.InvariantCulture),
                AccountingCsv.Text(e.AccountName),
                AccountingCsv.TaxId(e.TaxId),
                AccountingCsv.Text(e.Notes),
                e.Imported ? "importado" : "digitado",
            ]));
        }

        return csv.ToString();
    }

    private static string MethodName(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Dinheiro",
        PaymentMethod.Pix => "Pix",
        PaymentMethod.Boleto => "Boleto",
        PaymentMethod.CreditCard => "Cartão de crédito",
        PaymentMethod.DebitCard => "Cartão de débito",
        PaymentMethod.BankTransfer => "Transferência",
        _ => method.ToString(),
    };
}
