using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Domain.Common;
using Expense.Tracking.Reporting.Domain.Invoices;

namespace Expense.Tracking.Reporting.Application.Invoices;

/// <summary>Cartao como o accounts o devolve ao dono (so o que a fatura usa).</summary>
public sealed record CardInfo(
    Guid Id, Guid AccountId, string Label, string LastFourDigits, decimal CreditLimit, int ClosingDay, int DueDay, bool IsActive);

/// <summary>Compra ou estorno no cartao, lido da projecao.</summary>
public sealed record CardTransactionRecord(
    Guid TransactionId, string TransactionType, string Category, string? Description, decimal Amount, string Currency,
    DateOnly OccurredOn, int Installments);

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
    IReadOnlyList<InvoiceItemDto> Items);

/// <param name="CurrentDueMonth">Fatura aberta hoje (<c>yyyy-MM</c>), sempre presente na lista.</param>
public sealed record CardInvoicesDto(
    Guid CardId,
    string Label,
    string LastFourDigits,
    int ClosingDay,
    int DueDay,
    string Currency,
    string CurrentDueMonth,
    IReadOnlyList<InvoiceDto> Invoices);

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
/// <see cref="InvoiceCalculator"/>. Pagamentos entram com a INV-6.
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
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), options.TimeZone).DateTime);
        var current = cycle.DueMonthOf(today);

        var records = await projection.GetCardTransactionsAsync(ownerUserId, cardId, cancellationToken);
        var charges = records.Select(r => new CardCharge(
            r.TransactionId, r.Category, r.Amount, r.OccurredOn, r.Installments,
            IsRefund: r.TransactionType == "Income", Description: r.Description));

        var invoices = InvoiceCalculator.Calculate(cycle, charges, [], today).ToList();
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
                .Select(ToDto)
                .ToList());
    }

    private static InvoiceDto ToDto(Invoice invoice) => new(
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
            .ToList());

    private static string Month(DateOnly month) => month.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
}
