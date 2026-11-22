using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Domain.Common;
using Expense.Tracking.Reporting.Domain.Invoices;

namespace Expense.Tracking.Reporting.Application.Invoices;

/// <summary>Cartao como o accounts o devolve ao dono (so o que a fatura usa).</summary>
public sealed record CardInfo(
    Guid Id, Guid AccountId, string Label, string LastFourDigits, decimal CreditLimit, int ClosingDay, int DueDay, bool IsActive);

/// <summary>INV-8: cartao do usuario com o escopo PF/PJ da conta dele (nulo se a conta nao veio).</summary>
public sealed record ScopedCard(CardInfo Card, string? Scope);

/// <summary>Compra ou estorno no cartao, lido da projecao.</summary>
public sealed record CardTransactionRecord(
    Guid TransactionId, string TransactionType, string Category, string? Description, decimal Amount, string Currency,
    DateOnly OccurredOn, int Installments);

/// <summary>INV-6: pagamento de fatura, lido da projecao.</summary>
public sealed record CardPaymentRecord(Guid PaymentId, DateOnly InvoiceDueMonth, Guid AccountId, decimal Amount, DateOnly PaidOn);

/// <summary>Pagamento de uma fatura, para a tela listar e excluir (<c>DELETE /transactions/card-payments/{id}</c>).</summary>
public sealed record InvoicePaymentDto(Guid PaymentId, Guid AccountId, decimal Amount, DateOnly PaidOn);

public sealed record InvoiceItemDto(
    Guid TransactionId, string? Description, string Category, DateOnly PurchaseDate, decimal Amount, int Installment, int Installments);

/// <param name="DueMonth">Identifica a fatura: mes de vencimento, <c>yyyy-MM</c>.</param>
public sealed record InvoiceDto(
    string DueMonth,
    DateOnly StartDate,
    DateOnly ClosingDate,
    DateOnly DueDate,
    decimal Total,
    decimal Paid,
    decimal Remaining,
    string Status,
    IReadOnlyList<InvoiceItemDto> Items,
    IReadOnlyList<InvoicePaymentDto> Payments);

/// <param name="CurrentDueMonth">Fatura aberta hoje (<c>yyyy-MM</c>), sempre presente na lista.</param>
/// <param name="Outstanding">INV-10: falta pagar de todas as faturas (incluindo parcelas futuras), sem o recorte.</param>
/// <param name="AvailableLimit">INV-10: limite − <paramref name="Outstanding"/>; negativo quando passou do limite.</param>
public sealed record CardInvoicesDto(
    Guid CardId,
    string Label,
    string LastFourDigits,
    int ClosingDay,
    int DueDay,
    string Currency,
    string CurrentDueMonth,
    IReadOnlyList<InvoiceDto> Invoices,
    decimal CreditLimit,
    decimal Outstanding,
    decimal AvailableLimit);

/// <summary>Opcoes da leitura das faturas.</summary>
/// <param name="TimeZone">Fuso do "hoje" que decide se a fatura esta aberta, fechada ou vencida.</param>
public sealed record InvoiceOptions(TimeZoneInfo TimeZone)
{
    /// <summary>Sem <c>from</c>, a lista comeca 12 faturas antes da atual.</summary>
    public const int DefaultMonthsBack = 12;
}

/// <summary>
/// INV-4 (ADR-0005): faturas de um cartao. O ciclo vem do accounts, na hora, com o token do usuario
/// (cartao que o accounts nao devolve = 404); as compras vem da projecao; a regra e a do
/// <see cref="InvoiceCalculator"/>. Os pagamentos (INV-6) tambem vem da projecao e abatem a fatura
/// do mes de vencimento que informam.
/// </summary>
public sealed class GetCardInvoicesHandler(
    ICardDirectory cards, IProjectionQueries projection, InvoiceOptions options, TimeProvider timeProvider)
{
    public static readonly Error CardNotFound = Error.NotFound("card.not_found", "Cartao nao encontrado para o usuario.");

    public async Task<Result<CardInvoicesDto>> HandleAsync(
        Guid ownerUserId, Guid cardId, DateOnly? fromDueMonth, DateOnly? toDueMonth, CancellationToken cancellationToken)
    {
        var card = await cards.FindAsync(cardId, cancellationToken);
        if (card is null)
        {
            return Result.Failure<CardInvoicesDto>(CardNotFound);
        }

        var cycle = new CardCycle(card.ClosingDay, card.DueDay);
        var today = CardInvoiceSource.Today(timeProvider, options);
        var current = cycle.DueMonthOf(today);

        var loaded = await CardInvoiceSource.LoadAsync(projection, ownerUserId, card, today, cancellationToken);
        var records = loaded.Charges;
        var paymentsByMonth = loaded.Payments
            .GroupBy(p => CardCycle.FirstDayOf(p.InvoiceDueMonth))
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<InvoicePaymentDto>)g
                    .OrderBy(p => p.PaidOn)
                    .Select(p => new InvoicePaymentDto(p.PaymentId, p.AccountId, p.Amount, p.PaidOn))
                    .ToList());

        var invoices = loaded.Invoices.ToList();
        if (invoices.All(i => i.Period.DueMonth != current))
        {
            var period = cycle.PeriodOf(current);
            invoices.Add(new Invoice(period, 0m, 0m, 0m, InvoiceCalculator.StatusOf(period, 0m, 0m, today), []));
        }

        var from = fromDueMonth is { } f ? CardCycle.FirstDayOf(f) : current.AddMonths(-InvoiceOptions.DefaultMonthsBack);
        var to = toDueMonth is { } t ? CardCycle.FirstDayOf(t) : DateOnly.MaxValue;

        return new CardInvoicesDto(
            card.Id,
            card.Label,
            card.LastFourDigits,
            card.ClosingDay,
            card.DueDay,
            records.Select(r => r.Currency).FirstOrDefault() ?? "BRL",
            Month(current),
            invoices
                .Where(i => i.Period.DueMonth >= from && i.Period.DueMonth <= to)
                .OrderBy(i => i.Period.DueMonth)
                .Select(i => ToDto(i, paymentsByMonth.GetValueOrDefault(i.Period.DueMonth) ?? []))
                .ToList(),
            card.CreditLimit,
            loaded.Outstanding,
            card.CreditLimit - loaded.Outstanding);
    }

    /// <summary>Compras e estornos da projecao como a regra da fatura os le (receita no cartao = estorno).</summary>
    public static IEnumerable<CardCharge> ChargesOf(IEnumerable<CardTransactionRecord> records) =>
        records.Select(r => new CardCharge(
            r.TransactionId, r.Category, r.Amount, r.OccurredOn, r.Installments,
            IsRefund: r.TransactionType == "Income", Description: r.Description));

    private static InvoiceDto ToDto(Invoice invoice, IReadOnlyList<InvoicePaymentDto> payments) => new(
        Month(invoice.Period.DueMonth),
        invoice.Period.StartDate,
        invoice.Period.ClosingDate,
        invoice.Period.DueDate,
        invoice.Total,
        invoice.Paid,
        invoice.Remaining,
        invoice.Status.ToString(),
        invoice.Items
            .Select(i => new InvoiceItemDto(i.TransactionId, i.Description, i.Category, i.PurchaseDate, i.Amount, i.Installment, i.Installments))
            .ToList(),
        payments);

    private static string Month(DateOnly month) => month.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
}
