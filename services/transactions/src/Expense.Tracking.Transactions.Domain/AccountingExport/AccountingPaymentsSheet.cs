using System.Globalization;

namespace Expense.Tracking.Transactions.Domain.AccountingExport;

/// <summary>
/// Um pagamento de fatura como sai para o contador. <paramref name="InvoiceDueMonth"/> e o mes de
/// vencimento da fatura paga; <paramref name="TaxId"/>, o documento da conta de origem.
/// </summary>
public sealed record AccountingPayment(
    DateOnly PaidOn,
    string Card,
    DateOnly InvoiceDueMonth,
    string AccountName,
    string? TaxId,
    decimal Amount,
    string Currency);

/// <summary>
/// CTB-3 (ADR-0006, decisao 3): <c>pagamentos-fatura.csv</c>. O pagamento de fatura nao e
/// lancamento (ADR-0005, decisao 6), mas explica a saida do banco no dia em que foi pago.
/// </summary>
public static class AccountingPaymentsSheet
{
    public const string FileName = "pagamentos-fatura.csv";

    private static readonly string[] Header = ["Data", "Cartão", "Fatura", "Conta de origem", "CNPJ", "Valor", "Moeda"];

    public static string Write(IEnumerable<AccountingPayment> payments)
    {
        var csv = new System.Text.StringBuilder(AccountingCsv.Line(Header));
        foreach (var p in payments)
        {
            csv.Append(AccountingCsv.Line(
            [
                AccountingCsv.Date(p.PaidOn),
                AccountingCsv.Text(p.Card),
                p.InvoiceDueMonth.ToString("MM/yyyy", CultureInfo.InvariantCulture),
                AccountingCsv.Text(p.AccountName),
                AccountingCsv.TaxId(p.TaxId),
                AccountingCsv.Amount(p.Amount),
                p.Currency,
            ]));
        }

        return csv.ToString();
    }
}
