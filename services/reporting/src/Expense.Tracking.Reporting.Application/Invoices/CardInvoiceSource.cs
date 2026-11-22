using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Domain.Invoices;

namespace Expense.Tracking.Reporting.Application.Invoices;

/// <summary>Faturas de um cartao calculadas a partir da projecao, com o que veio de la.</summary>
public sealed record CardInvoices(
    IReadOnlyList<Invoice> Invoices, IReadOnlyList<CardTransactionRecord> Charges, IReadOnlyList<CardPaymentRecord> Payments)
{
    /// <summary>
    /// INV-10 (ADR-0005, decisao 9): o que ainda falta pagar de todas as faturas, incluindo as
    /// parcelas futuras. Pagamento acima do total de uma fatura nao abate as outras.
    /// </summary>
    public decimal Outstanding => Invoices.Sum(i => i.Remaining);
}

/// <summary>
/// Le compras e pagamentos de um cartao na projecao e aplica a regra da fatura (INV-3). Um ponto so
/// para a fatura (INV-4), a previsao (INV-8) e o limite disponivel (INV-10).
/// </summary>
public static class CardInvoiceSource
{
    public static async Task<CardInvoices> LoadAsync(
        IProjectionQueries projection, Guid ownerUserId, CardInfo card, DateOnly today, CancellationToken cancellationToken)
    {
        var charges = await projection.GetCardTransactionsAsync(ownerUserId, card.Id, cancellationToken);
        var payments = await projection.GetCardPaymentsAsync(ownerUserId, card.Id, cancellationToken);
        var invoices = InvoiceCalculator.Calculate(
            new CardCycle(card.ClosingDay, card.DueDay),
            GetCardInvoicesHandler.ChargesOf(charges),
            payments.Select(p => new InvoicePayment(p.InvoiceDueMonth, p.Amount)),
            today);
        return new CardInvoices(invoices, charges, payments);
    }

    /// <summary>"Hoje" no fuso configurado: decide se a fatura esta aberta, fechada ou vencida.</summary>
    public static DateOnly Today(TimeProvider timeProvider, InvoiceOptions options) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), options.TimeZone).DateTime);
}
