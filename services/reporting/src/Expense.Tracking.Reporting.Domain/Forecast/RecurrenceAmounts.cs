namespace Expense.Tracking.Reporting.Domain.Forecast;

/// <summary>RJT-3 (ADR-0010, decisao 1): o valor estimado de uma regra a partir de um periodo.</summary>
public sealed record RecurrenceAmountFrom(string FromPeriod, decimal Amount);

/// <summary>
/// RJT-3: valor de um periodo pelas vigencias da regra: o da ultima com inicio ate ele. Os periodos de uma
/// regra tem o mesmo formato (<c>yyyy-MM</c> ou <c>yyyy</c>), entao a ordem do texto e a do tempo.
/// </summary>
public static class RecurrenceAmounts
{
    public static decimal For(IReadOnlyList<RecurrenceAmountFrom> amounts, decimal fallback, string period)
    {
        decimal? found = null;
        foreach (var amount in amounts.OrderBy(a => a.FromPeriod, StringComparer.Ordinal))
        {
            if (string.CompareOrdinal(amount.FromPeriod, period) > 0)
            {
                break;
            }

            found = amount.Amount;
        }

        return found ?? fallback;
    }
}
