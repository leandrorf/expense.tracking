namespace Expense.Tracking.Accounts.Domain.Cards;

/// <summary>
/// CYC (ADR-0012, decisao 1): um ciclo do cartao (dia de fechamento e de vencimento) valendo a partir da fatura
/// que vence em <see cref="FromDueMonth"/> (dia 1). Nulo = o ciclo da criacao, que vale desde sempre.
/// </summary>
public sealed record CardCycleTerm(DateOnly? FromDueMonth, int ClosingDay, int DueDay)
{
    /// <summary>Vence no mes seguinte ao fechamento (ex.: fecha dia 25, vence dia 5).</summary>
    private bool DueInFollowingMonth => DueDay <= ClosingDay;

    /// <summary>Fechamento da fatura que vence em <paramref name="dueMonth"/> (dia 1), por este ciclo.</summary>
    public DateOnly ClosingDateOf(DateOnly dueMonth)
    {
        var closingMonth = DueInFollowingMonth ? dueMonth.AddMonths(-1) : dueMonth;
        return closingMonth.AddDays(ClosingDay - 1);
    }

    public DateOnly DueDateOf(DateOnly dueMonth) => dueMonth.AddDays(DueDay - 1);
}

/// <summary>Datas de uma fatura: inicio (dia seguinte ao fechamento anterior), fechamento e vencimento.</summary>
public sealed record CardInvoicePeriod(DateOnly DueMonth, DateOnly StartDate, DateOnly ClosingDate, DateOnly DueDate);

/// <summary>
/// CYC (ADR-0012, decisao 2): os ciclos de um cartao com vigencia e os periodos encadeados. A fatura de um mes de
/// vencimento segue o ciclo da ultima vigencia com inicio ate ele. A fatura de um ciclo que fecharia no mesmo dia
/// ou depois do primeiro fechamento do ciclo seguinte nao existe (o mes de vencimento pula). O inicio de cada
/// fatura e o dia seguinte ao fechamento da fatura anterior que existe. Copia da regra do <c>reporting</c>
/// (decisao 6), com os mesmos testes de exemplo.
/// </summary>
public sealed class CardCycleSchedule
{
    /// <summary>Quantos meses olhar para tras ate achar a fatura anterior que existe.</summary>
    private const int MaxLookBack = 3;

    private readonly IReadOnlyList<CardCycleTerm> _terms;

    /// <param name="terms">O primeiro com <c>FromDueMonth</c> nulo; os outros em qualquer ordem.</param>
    public CardCycleSchedule(IEnumerable<CardCycleTerm> terms)
    {
        _terms = terms.OrderBy(t => t.FromDueMonth ?? DateOnly.MinValue).ToList();
        if (_terms.Count == 0 || _terms[0].FromDueMonth is not null)
        {
            throw new ArgumentException("O primeiro ciclo vale desde sempre (FromDueMonth nulo).", nameof(terms));
        }
    }

    public IReadOnlyList<CardCycleTerm> Terms => _terms;

    /// <summary>O ciclo da fatura que vence em <paramref name="dueMonth"/>.</summary>
    public CardCycleTerm TermOf(DateOnly dueMonth) => _terms[IndexOf(FirstDayOf(dueMonth))];

    /// <summary>Se ha fatura vencendo em <paramref name="dueMonth"/> (nao some na troca para o ciclo seguinte).</summary>
    public bool Exists(DateOnly dueMonth)
    {
        var month = FirstDayOf(dueMonth);
        var index = IndexOf(month);
        if (index == _terms.Count - 1)
        {
            return true;
        }

        var next = _terms[index + 1];
        return _terms[index].ClosingDateOf(month) < next.ClosingDateOf(next.FromDueMonth!.Value);
    }

    /// <summary>Fechamento da fatura de <paramref name="dueMonth"/> pelo ciclo dela (exista ou nao).</summary>
    public DateOnly ClosingDateOf(DateOnly dueMonth) => TermOf(dueMonth).ClosingDateOf(FirstDayOf(dueMonth));

    /// <summary>Datas da fatura de <paramref name="dueMonth"/>; o inicio vem da fatura anterior que existe.</summary>
    public CardInvoicePeriod PeriodOf(DateOnly dueMonth)
    {
        var month = FirstDayOf(dueMonth);
        var term = TermOf(month);
        var previous = month.AddMonths(-1);
        for (var i = 0; i < MaxLookBack && !Exists(previous); i++)
        {
            previous = previous.AddMonths(-1);
        }

        return new CardInvoicePeriod(month, ClosingDateOf(previous).AddDays(1), term.ClosingDateOf(month), term.DueDateOf(month));
    }

    /// <summary>Mes de vencimento (dia 1) da fatura que existe e cujo periodo contem <paramref name="date"/>.</summary>
    public DateOnly DueMonthOf(DateOnly date)
    {
        var start = FirstDayOf(date).AddMonths(-1);
        return Enumerable.Range(0, 5)
            .Select(i => start.AddMonths(i))
            .Where(m => Exists(m) && ClosingDateOf(m) >= date)
            .OrderBy(ClosingDateOf)
            .First();
    }

    public static DateOnly FirstDayOf(DateOnly date) => new(date.Year, date.Month, 1);

    private int IndexOf(DateOnly month)
    {
        for (var i = _terms.Count - 1; i > 0; i--)
        {
            if (_terms[i].FromDueMonth <= month)
            {
                return i;
            }
        }

        return 0;
    }
}
