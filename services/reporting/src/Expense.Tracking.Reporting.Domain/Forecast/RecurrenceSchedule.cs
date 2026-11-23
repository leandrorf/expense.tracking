using System.Globalization;

namespace Expense.Tracking.Reporting.Domain.Forecast;

/// <summary>Uma ocorrencia da agenda: o periodo que a identifica e a data em que cai.</summary>
public readonly record struct ScheduledOccurrence(string Period, DateOnly ScheduledOn);

/// <summary>
/// REC-4 (ADR-0008, decisoes 3 e 4): agenda de uma regra de recorrencia, do lado do reporting. E uma
/// copia propria da regra do transactions (o reporting depende so do formato do feed, ADR-0001): a
/// mensal repete no dia do inicio todo mes, a anual no dia e mes do inicio todo ano; mes sem aquele
/// dia usa o ultimo dia. O periodo (<c>yyyy-MM</c> ou <c>yyyy</c>) identifica a ocorrencia.
/// </summary>
public static class RecurrenceSchedule
{
    public const string Monthly = "Monthly";
    public const string Yearly = "Yearly";

    public static string PeriodOf(string frequency, DateOnly date) => frequency == Yearly
        ? date.Year.ToString("0000", CultureInfo.InvariantCulture)
        : date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Ocorrencias da agenda com data entre <paramref name="from"/> e <paramref name="to"/> (inclusive).</summary>
    public static IEnumerable<ScheduledOccurrence> Occurrences(
        string frequency, DateOnly startsOn, DateOnly? endsOn, DateOnly from, DateOnly to)
    {
        var start = from < startsOn ? startsOn : from;
        var last = endsOn is { } end && end < to ? end : to;
        if (start > last)
        {
            yield break;
        }

        var yearly = frequency == Yearly;
        var cursor = yearly ? new DateOnly(start.Year, 1, 1) : new DateOnly(start.Year, start.Month, 1);
        while (cursor <= last)
        {
            var date = yearly
                ? Clamp(cursor.Year, startsOn.Month, startsOn.Day)
                : Clamp(cursor.Year, cursor.Month, startsOn.Day);
            if (date >= start && date <= last)
            {
                yield return new ScheduledOccurrence(PeriodOf(frequency, date), date);
            }

            cursor = yearly ? cursor.AddYears(1) : cursor.AddMonths(1);
        }
    }

    private static DateOnly Clamp(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
}
