namespace Expense.Tracking.Reporting.Domain.Invoices;

/// <summary>
/// CYC (ADR-0012, decisao 1): um ciclo do cartao valendo a partir da fatura que vence em <see cref="FromDueMonth"/>
/// (dia 1). Nulo = o ciclo da criacao, que vale desde sempre.
/// </summary>
public sealed record CardCycleTerm(DateOnly? FromDueMonth, int ClosingDay, int DueDay)
{
    public CardCycle Cycle { get; } = new(ClosingDay, DueDay);
}

/// <summary>
/// CYC-4 (ADR-0012, decisao 2; substitui a regra 4 da ADR-0005): os ciclos de um cartao com vigencia e os periodos
/// encadeados. A fatura de um mes de vencimento segue o ciclo da ultima vigencia com inicio ate ele. A fatura de um
/// ciclo que fecharia no mesmo dia ou depois do primeiro fechamento do ciclo seguinte nao existe (o mes de
/// vencimento pula). O inicio de cada fatura e o dia seguinte ao fechamento da fatura anterior que existe. Mesma
/// regra (e mesmos exemplos de teste) da copia no <c>accounts</c> (decisao 6).
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

    /// <summary>Um ciclo so (cartao sem troca).</summary>
    public static CardCycleSchedule Single(int closingDay, int dueDay) => new([new CardCycleTerm(null, closingDay, dueDay)]);

    public IReadOnlyList<CardCycleTerm> Terms => _terms;

    public CardCycleTerm TermOf(DateOnly dueMonth) => _terms[IndexOf(CardCycle.FirstDayOf(dueMonth))];

    /// <summary>Se ha fatura vencendo em <paramref name="dueMonth"/> (nao some na troca para o ciclo seguinte).</summary>
    public bool Exists(DateOnly dueMonth)
    {
        var month = CardCycle.FirstDayOf(dueMonth);
        var index = IndexOf(month);
        if (index == _terms.Count - 1)
        {
            return true;
        }

        var next = _terms[index + 1];
        return ClosingOf(_terms[index], month) < ClosingOf(next, next.FromDueMonth!.Value);
    }

    /// <summary>Datas da fatura de <paramref name="dueMonth"/>; o inicio vem da fatura anterior que existe.</summary>
    public InvoicePeriod PeriodOf(DateOnly dueMonth)
    {
        var month = CardCycle.FirstDayOf(dueMonth);
        var own = TermOf(month).Cycle.PeriodOf(month);
        var previous = month.AddMonths(-1);
        for (var i = 0; i < MaxLookBack && !Exists(previous); i++)
        {
            previous = previous.AddMonths(-1);
        }

        return own with { StartDate = ClosingOf(TermOf(previous), previous).AddDays(1) };
    }

    /// <summary>Mes de vencimento (dia 1) da fatura que existe e cujo periodo contem <paramref name="date"/>.</summary>
    public DateOnly DueMonthOf(DateOnly date)
    {
        var start = CardCycle.FirstDayOf(date).AddMonths(-1);
        return Enumerable.Range(0, 5)
            .Select(i => start.AddMonths(i))
            .Where(m => Exists(m) && ClosingOf(TermOf(m), m) >= date)
            .OrderBy(m => ClosingOf(TermOf(m), m))
            .First();
    }

    /// <summary>A proxima fatura que existe depois de <paramref name="dueMonth"/> (a parcela seguinte cai nela).</summary>
    public DateOnly NextDueMonth(DateOnly dueMonth)
    {
        var month = CardCycle.FirstDayOf(dueMonth).AddMonths(1);
        for (var i = 0; i < MaxLookBack && !Exists(month); i++)
        {
            month = month.AddMonths(1);
        }

        return month;
    }

    private static DateOnly ClosingOf(CardCycleTerm term, DateOnly month) => term.Cycle.PeriodOf(month).ClosingDate;

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
