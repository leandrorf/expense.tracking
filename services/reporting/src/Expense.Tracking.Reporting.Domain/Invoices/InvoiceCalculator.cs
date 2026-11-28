namespace Expense.Tracking.Reporting.Domain.Invoices;

/// <summary>Situacao da fatura (ADR-0005, decisao 7): calculada na leitura, nunca guardada.</summary>
public enum InvoiceStatus
{
    /// <summary>Ate o dia de fechamento, inclusive: ainda recebe compras.</summary>
    Open = 1,

    /// <summary>Entre o fechamento e o vencimento, sem pagamento.</summary>
    Closed = 2,

    /// <summary>Pagamentos cobrem o total.</summary>
    Paid = 3,

    /// <summary>Pagamentos cobrem parte do total, ainda no prazo.</summary>
    PartiallyPaid = 4,

    /// <summary>Depois do vencimento sem cobrir o total.</summary>
    Overdue = 5
}

/// <summary>
/// Um lancamento no cartao como a fatura o enxerga. <paramref name="Amount"/> e sempre positivo,
/// o sentido vem de <paramref name="IsRefund"/> (receita no cartao). Estorno e sempre a vista.
/// <paramref name="Description"/> e nula so em lancamento ainda sem os detalhes do cartao (INV-2).
/// </summary>
public sealed record CardCharge(
    Guid TransactionId, string Category, decimal Amount, DateOnly OccurredOn, int Installments = 1, bool IsRefund = false,
    string? Description = null);

/// <summary>Pagamento registrado para a fatura de um mes de vencimento.</summary>
public sealed record InvoicePayment(DateOnly DueMonth, decimal Amount);

/// <param name="Amount">Positivo para compra ou parcela; negativo para estorno (abate do total).</param>
/// <param name="Installment">Numero da parcela (1 a vista).</param>
public sealed record InvoiceItem(
    Guid TransactionId, string Category, DateOnly PurchaseDate, decimal Amount, int Installment, int Installments,
    string? Description = null);

/// <param name="Remaining">O que falta pagar; nunca negativo (pagamento acima do total nao gera credito).</param>
public sealed record Invoice(
    InvoicePeriod Period,
    decimal Total,
    decimal Paid,
    decimal Remaining,
    InvoiceStatus Status,
    IReadOnlyList<InvoiceItem> Items);

/// <summary>
/// INV-3: monta as faturas de um cartao a partir das compras, estornos e pagamentos. Funcao pura:
/// o chamador entrega os lancamentos do cartao (de um dono) e os pagamentos; a fatura de cada
/// parcela vem do <see cref="CardCycle"/>. Gera uma fatura para cada mes de vencimento com item ou
/// pagamento, em ordem; quem chama recorta o periodo que quer mostrar.
/// </summary>
public static class InvoiceCalculator
{
    public static IReadOnlyList<Invoice> Calculate(
        CardCycle cycle, IEnumerable<CardCharge> charges, IEnumerable<InvoicePayment> payments, DateOnly today) =>
        Calculate(CardCycleSchedule.Single(cycle.ClosingDay, cycle.DueDay), charges, payments, today);

    /// <summary>CYC-4 (ADR-0012, decisao 2): o mesmo calculo com os ciclos de cada periodo.</summary>
    public static IReadOnlyList<Invoice> Calculate(
        CardCycleSchedule cycle, IEnumerable<CardCharge> charges, IEnumerable<InvoicePayment> payments, DateOnly today)
    {
        var items = charges
            .SelectMany(charge => ItemsOf(cycle, charge))
            .GroupBy(x => x.DueMonth)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Item).ToList());

        var paidByMonth = payments
            .GroupBy(p => CardCycle.FirstDayOf(p.DueMonth))
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        return items.Keys
            .Union(paidByMonth.Keys)
            .Order()
            .Select(dueMonth =>
            {
                var monthItems = items.GetValueOrDefault(dueMonth) ?? [];
                var total = monthItems.Sum(i => i.Amount);
                var paid = paidByMonth.GetValueOrDefault(dueMonth);
                var period = cycle.PeriodOf(dueMonth);
                return new Invoice(
                    period, total, paid, Math.Max(total - paid, 0m), StatusOf(period, total, paid, today),
                    monthItems.OrderBy(i => i.PurchaseDate).ThenBy(i => i.TransactionId).ToList());
            })
            .ToList();
    }

    /// <summary>
    /// Ordem das regras: aberta ate o fechamento (ainda recebe compras, mesmo com pagamento
    /// antecipado); depois, paga se os pagamentos cobrem o total; vencida se passou do vencimento;
    /// parcialmente paga se ha pagamento; fechada se nao ha.
    /// </summary>
    public static InvoiceStatus StatusOf(InvoicePeriod period, decimal total, decimal paid, DateOnly today)
    {
        if (today <= period.ClosingDate)
        {
            return InvoiceStatus.Open;
        }

        if (paid >= total)
        {
            return InvoiceStatus.Paid;
        }

        if (today > period.DueDate)
        {
            return InvoiceStatus.Overdue;
        }

        return paid > 0 ? InvoiceStatus.PartiallyPaid : InvoiceStatus.Closed;
    }

    /// <summary>CYC-4: cada parcela cai na fatura seguinte que existe (um mes de vencimento pode pular na troca de ciclo).</summary>
    private static IEnumerable<(DateOnly DueMonth, InvoiceItem Item)> ItemsOf(CardCycleSchedule cycle, CardCharge charge)
    {
        var firstDueMonth = cycle.DueMonthOf(charge.OccurredOn);
        if (charge.IsRefund)
        {
            yield return (firstDueMonth, new InvoiceItem(
                charge.TransactionId, charge.Category, charge.OccurredOn, -charge.Amount, 1, 1, charge.Description));
            yield break;
        }

        var parts = Installments.Split(charge.Amount, charge.Installments);
        var dueMonth = firstDueMonth;
        for (var i = 0; i < parts.Count; i++)
        {
            yield return (dueMonth, new InvoiceItem(
                charge.TransactionId, charge.Category, charge.OccurredOn, parts[i], i + 1, parts.Count, charge.Description));
            dueMonth = cycle.NextDueMonth(dueMonth);
        }
    }
}
