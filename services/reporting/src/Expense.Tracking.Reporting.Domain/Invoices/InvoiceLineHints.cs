namespace Expense.Tracking.Reporting.Domain.Invoices;

/// <summary>
/// RCN-4 (ADR-0011, decisao 4): credito da fatura do banco que e pagamento (fica fora da comparacao, ja e tratado
/// pela INV-11); os outros creditos sao estornos e entram.
/// </summary>
public static class InvoiceLineHints
{
    private static readonly string[] PaymentWords = ["PAGAMENTO", "PAGTO", "PGTO", "PAG FATURA", "PAGAMENTO RECEBIDO"];

    public static bool IsPayment(string description) =>
        PaymentWords.Any(w => description.Contains(w, StringComparison.OrdinalIgnoreCase));
}
