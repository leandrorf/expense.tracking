namespace Expense.Tracking.Reporting.Domain.Projection;

/// <summary>
/// INV-6 (ADR-0005, decisao 6): pagamento de fatura como o reporting o conhece, montado pelos
/// eventos <c>CardInvoicePaid</c>/<c>CardInvoicePaymentDeleted</c>. Fica fora de
/// <see cref="ProjectedTransaction"/>: nunca entra no Resumo, nos orcamentos nem no "Sem orcamento".
/// </summary>
public sealed class ProjectedCardPayment
{
    public Guid PaymentId { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid CreditCardId { get; set; }

    /// <summary>Fatura paga, pelo mes de vencimento (dia 1).</summary>
    public DateOnly InvoiceDueMonth { get; set; }

    /// <summary>Conta de onde saiu o dinheiro.</summary>
    public Guid AccountId { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public DateOnly PaidOn { get; set; }

    /// <summary>Sequencia do ultimo evento do feed aplicado a esta linha.</summary>
    public long LastEventSequence { get; set; }
}
