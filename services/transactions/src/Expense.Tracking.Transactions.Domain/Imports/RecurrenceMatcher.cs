using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Domain.Imports;

/// <summary>
/// REC-9: uma ocorrencia em aberto de uma regra da conta do extrato (inclusive as "a lancar"), candidata
/// a ser a linha importada.
/// </summary>
public sealed record RecurrenceCandidate(
    Guid RecurrenceId, string Period, DateOnly ScheduledOn, TransactionType Type, decimal Amount, string Description, string Category);

/// <summary>
/// REC-9 (ADR-0008, decisao 11): casa as linhas da previa com ocorrencias em aberto. Uma linha casa com
/// uma ocorrencia do mesmo tipo, com valor ate <see cref="AmountTolerance"/> diferente do estimado e data
/// ate <see cref="DayTolerance"/> dias da agendada; havendo mais de uma, a de data mais proxima (e, no
/// empate, a de valor mais proximo). Cada ocorrencia vai para uma linha so, na ordem das linhas.
/// </summary>
public static class RecurrenceMatcher
{
    public const decimal AmountTolerance = 0.10m;

    public const int DayTolerance = 5;

    /// <summary>Para cada linha que casou, a ocorrencia sugerida.</summary>
    public static IReadOnlyDictionary<int, RecurrenceCandidate> Match(
        IEnumerable<(int Number, TransactionType Type, decimal Amount, DateOnly OccurredOn)> lines,
        IEnumerable<RecurrenceCandidate> candidates)
    {
        var open = candidates.ToList();
        var matches = new Dictionary<int, RecurrenceCandidate>();
        foreach (var line in lines.OrderBy(l => l.Number))
        {
            var best = open
                .Where(c => c.Type == line.Type
                    && Math.Abs(line.Amount - c.Amount) <= c.Amount * AmountTolerance
                    && Math.Abs(line.OccurredOn.DayNumber - c.ScheduledOn.DayNumber) <= DayTolerance)
                .OrderBy(c => Math.Abs(line.OccurredOn.DayNumber - c.ScheduledOn.DayNumber))
                .ThenBy(c => Math.Abs(line.Amount - c.Amount))
                .FirstOrDefault();
            if (best is null)
            {
                continue;
            }

            matches[line.Number] = best;
            open.Remove(best);
        }

        return matches;
    }
}
