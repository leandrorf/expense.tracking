namespace Expense.Tracking.Reporting.Domain.Invoices;

/// <summary>Datas de uma fatura: o ciclo vai de <see cref="StartDate"/> a <see cref="ClosingDate"/>, inclusive.</summary>
/// <param name="DueMonth">Identifica a fatura: primeiro dia do mes de vencimento, como o banco a chama.</param>
public sealed record InvoicePeriod(DateOnly DueMonth, DateOnly StartDate, DateOnly ClosingDate, DateOnly DueDate);

/// <summary>
/// INV-3 (ADR-0005, decisao 4): ciclo de cobranca de um cartao. Compra ate o dia de fechamento,
/// inclusive, cai na fatura que fecha naquele mes; depois dele, na do mes seguinte. O vencimento e
/// no mesmo mes do fechamento quando o dia de vencimento vem depois do de fechamento, e no mes
/// seguinte quando nao. Dias de 1 a 28 (todo mes tem o dia), sem calendario de dias uteis.
/// </summary>
public sealed record CardCycle
{
    public const int MinDay = 1;
    public const int MaxDay = 28;

    public CardCycle(int closingDay, int dueDay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(closingDay, MinDay);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(closingDay, MaxDay);
        ArgumentOutOfRangeException.ThrowIfLessThan(dueDay, MinDay);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dueDay, MaxDay);
        ClosingDay = closingDay;
        DueDay = dueDay;
    }

    public int ClosingDay { get; }

    public int DueDay { get; }

    /// <summary>Vence no mes seguinte ao fechamento (ex.: fecha dia 25, vence dia 5).</summary>
    private bool DueInFollowingMonth => DueDay <= ClosingDay;

    /// <summary>Mes de vencimento (primeiro dia do mes) da fatura em que cai uma compra feita em <paramref name="date"/>.</summary>
    public DateOnly DueMonthOf(DateOnly date)
    {
        var closingMonth = FirstDayOf(date);
        if (date.Day > ClosingDay)
        {
            closingMonth = closingMonth.AddMonths(1);
        }

        return DueInFollowingMonth ? closingMonth.AddMonths(1) : closingMonth;
    }

    /// <summary>Datas da fatura que vence em <paramref name="dueMonth"/> (qualquer dia do mes serve).</summary>
    public InvoicePeriod PeriodOf(DateOnly dueMonth)
    {
        var due = FirstDayOf(dueMonth);
        var closingMonth = DueInFollowingMonth ? due.AddMonths(-1) : due;
        var closingDate = closingMonth.AddDays(ClosingDay - 1);
        var previousClosing = closingMonth.AddMonths(-1).AddDays(ClosingDay - 1);
        return new InvoicePeriod(due, previousClosing.AddDays(1), closingDate, due.AddDays(DueDay - 1));
    }

    public static DateOnly FirstDayOf(DateOnly date) => new(date.Year, date.Month, 1);
}
