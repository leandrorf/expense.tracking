namespace Expense.Tracking.Reporting.Domain.Forecast;

/// <summary>Escopo de uma linha da previsao: <c>Personal</c> (PF) ou <c>Business</c> (PJ); nulo = desconhecido.</summary>
public static class ForecastScopes
{
    public const string Personal = "Personal";
    public const string Business = "Business";
}

/// <summary>
/// Lancamento fora do cartao (entra pela data). <see cref="IsIncome"/> = receita. <see cref="Recurring"/>
/// = ocorrencia em aberto de uma recorrencia (REC-4), ainda nao lancada.
/// </summary>
public sealed record CashMovement(string? Scope, bool IsIncome, decimal Amount, DateOnly OccurredOn, bool Recurring = false);

/// <summary>
/// Fatura de um cartao, no escopo da conta do cartao, pelo mes de vencimento (dia 1).
/// <see cref="Recurring"/> = parte da fatura que vem de ocorrencias recorrentes em aberto (REC-4).
/// </summary>
public sealed record ForecastInvoice(string? Scope, DateOnly DueMonth, decimal Total, decimal Paid, decimal Remaining, bool Recurring = false);

/// <summary>
/// Fluxo de um mes num escopo. <see cref="Result"/> = entradas − saidas − faturas (total), para o
/// resultado do mes nao mudar quando a fatura e paga antes; <see cref="InvoicesRemaining"/> e o que
/// ainda falta pagar (fatura paga = 0).
/// </summary>
/// <remarks>
/// REC-4 (ADR-0008, decisao 9): <see cref="RecurringIncome"/>, <see cref="RecurringExpense"/> e
/// <see cref="RecurringInvoices"/> sao a parte recorrente (ocorrencias em aberto) que ja esta dentro de
/// <see cref="Income"/>, <see cref="Expense"/> e <see cref="Invoices"/>.
/// </remarks>
public sealed record MonthFlow(
    decimal Income, decimal Expense, decimal Invoices, decimal InvoicesPaid, decimal InvoicesRemaining,
    decimal RecurringIncome = 0m, decimal RecurringExpense = 0m, decimal RecurringInvoices = 0m)
{
    public static readonly MonthFlow Empty = new(0m, 0m, 0m, 0m, 0m);

    public decimal Result => Income - Expense - Invoices;
}

/// <summary>Um mes da previsao: PF, PJ e o total (que inclui o que nao tem escopo conhecido).</summary>
public sealed record ForecastMonth(DateOnly Month, MonthFlow Personal, MonthFlow Business, MonthFlow Total);

/// <summary>
/// INV-8 (ADR-0005, decisao 8): previsao de caixa mes a mes. Lancamentos fora do cartao entram pela
/// data; o cartao entra so pela fatura, no mes de vencimento (cada parcela na sua fatura), nunca pela
/// data da compra; pagamento de fatura nao entra de novo (ja esta na fatura). So o que esta lancado.
/// </summary>
public static class CashForecastCalculator
{
    public const int DefaultMonths = 6;
    public const int MaxMonths = 12;

    public static IReadOnlyList<ForecastMonth> Calculate(
        DateOnly firstMonth, int months, IEnumerable<CashMovement> movements, IEnumerable<ForecastInvoice> invoices)
    {
        if (months is < 1 or > MaxMonths)
        {
            throw new ArgumentOutOfRangeException(nameof(months), months, $"De 1 a {MaxMonths} meses.");
        }

        var start = new DateOnly(firstMonth.Year, firstMonth.Month, 1);
        var cash = movements.ToList();
        var bills = invoices.ToList();

        return Enumerable.Range(0, months)
            .Select(offset =>
            {
                var month = start.AddMonths(offset);
                var monthCash = cash.Where(m => m.OccurredOn.Year == month.Year && m.OccurredOn.Month == month.Month).ToList();
                var monthBills = bills.Where(i => i.DueMonth == month).ToList();
                return new ForecastMonth(
                    month,
                    FlowOf(monthCash.Where(m => m.Scope == ForecastScopes.Personal), monthBills.Where(i => i.Scope == ForecastScopes.Personal)),
                    FlowOf(monthCash.Where(m => m.Scope == ForecastScopes.Business), monthBills.Where(i => i.Scope == ForecastScopes.Business)),
                    FlowOf(monthCash, monthBills));
            })
            .ToList();
    }

    private static MonthFlow FlowOf(IEnumerable<CashMovement> cash, IEnumerable<ForecastInvoice> bills)
    {
        var movements = cash.ToList();
        var invoices = bills.ToList();
        return new MonthFlow(
            movements.Where(m => m.IsIncome).Sum(m => m.Amount),
            movements.Where(m => !m.IsIncome).Sum(m => m.Amount),
            invoices.Sum(i => i.Total),
            invoices.Sum(i => i.Paid),
            invoices.Sum(i => i.Remaining),
            movements.Where(m => m.IsIncome && m.Recurring).Sum(m => m.Amount),
            movements.Where(m => !m.IsIncome && m.Recurring).Sum(m => m.Amount),
            invoices.Where(i => i.Recurring).Sum(i => i.Total));
    }
}
