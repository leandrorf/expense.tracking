using System.Globalization;

namespace Expense.Tracking.Transactions.Domain.Recurrences;

/// <summary>
/// RJT-6 (ADR-0010, decisao 6): variacao mensal de um indice oficial (em %), como o Banco Central publica.
/// <see cref="Month"/> e <c>yyyy-MM</c>. Uma linha por (indice, mes); o valor publicado nao muda depois.
/// </summary>
public sealed class EconomicIndexValue
{
    private EconomicIndexValue()
    {
        Month = string.Empty;
    }

    public EconomicIndex Index { get; private set; }

    public string Month { get; private set; }

    /// <summary>Variacao no mes, em % (0,42 = 0,42%).</summary>
    public decimal Rate { get; private set; }

    public DateTimeOffset FetchedAtUtc { get; private set; }

    public static EconomicIndexValue Create(EconomicIndex index, string month, decimal rate, DateTimeOffset fetchedAtUtc) => new()
    {
        Index = index,
        Month = month,
        Rate = rate,
        FetchedAtUtc = fetchedAtUtc,
    };

    /// <summary>Codigo da serie no SGS do Banco Central.</summary>
    public static int SgsSeriesOf(EconomicIndex index) => index switch
    {
        EconomicIndex.IPCA => 433,
        EconomicIndex.INPC => 188,
        EconomicIndex.IGPM => 189,
        _ => throw new ArgumentOutOfRangeException(nameof(index), index, "Indice sem serie no SGS."),
    };

    /// <summary><c>yyyy-MM</c> de uma data.</summary>
    public static string MonthOf(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
