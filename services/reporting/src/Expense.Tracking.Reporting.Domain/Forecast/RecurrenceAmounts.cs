namespace Expense.Tracking.Reporting.Domain.Forecast;

/// <summary>RJT-3 (ADR-0010, decisao 1): o valor estimado de uma regra a partir de um periodo.</summary>
public sealed record RecurrenceAmountFrom(string FromPeriod, decimal Amount);

/// <summary>RJT-8 (ADR-0010, decisao 8): reajuste anual da regra com a taxa estimada (%).</summary>
public sealed record RecurrenceAdjustmentEstimate(int AnniversaryMonth, decimal EstimatedRate, bool NoReduction);

/// <summary>RJT-8: reajuste estimado: a partir de <see cref="FromPeriod"/>, <see cref="Amount"/> no lugar de <see cref="Base"/>.</summary>
public sealed record EstimatedAdjustment(string FromPeriod, decimal Base, decimal Amount);

/// <summary>RJT-8: valor de uma ocorrencia na previsao e quanto dele e reajuste estimado.</summary>
public readonly record struct ForecastAmount(decimal Amount, decimal EstimatedPart);

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

    /// <summary>
    /// RJT-8 (ADR-0010, decisao 8): o unico reajuste estimado a frente: o do aniversario mais recente (ate
    /// hoje) se ainda nao tem vigencia, senao o do proximo; nunca no periodo do inicio. Sobre o valor vigente
    /// antes dele, pela taxa estimada, a centavos (meio para cima), com o "nao reduzir".
    /// </summary>
    public static EstimatedAdjustment? Estimate(
        IReadOnlyList<RecurrenceAmountFrom> amounts, decimal fallback, string frequency, DateOnly startsOn, DateOnly today,
        RecurrenceAdjustmentEstimate? adjustment)
    {
        if (adjustment is null)
        {
            return null;
        }

        var first = RecurrenceSchedule.PeriodOf(frequency, startsOn);
        var recent = today.Month >= adjustment.AnniversaryMonth ? today.Year : today.Year - 1;
        foreach (var year in new[] { recent, recent + 1 })
        {
            var period = RecurrenceSchedule.PeriodOf(frequency, new DateOnly(year, adjustment.AnniversaryMonth, 1));
            if (string.CompareOrdinal(period, first) <= 0 || amounts.Any(a => a.FromPeriod == period))
            {
                continue;
            }

            var before = amounts.Where(a => string.CompareOrdinal(a.FromPeriod, period) < 0)
                .OrderBy(a => a.FromPeriod, StringComparer.Ordinal).LastOrDefault()?.Amount ?? fallback;
            var adjusted = decimal.Round(before * (1m + adjustment.EstimatedRate / 100m), 2, MidpointRounding.AwayFromZero);
            return new EstimatedAdjustment(period, before, adjustment.NoReduction && adjusted < before ? before : adjusted);
        }

        return null;
    }

    /// <summary>
    /// RJT-8: valor do periodo com o reajuste estimado: do aniversario estimado em diante, enquanto nenhuma
    /// vigencia posterior a ele ja valer, e o estimado; a parte estimada e a diferenca para o valor sem ele.
    /// </summary>
    public static ForecastAmount ForForecast(
        IReadOnlyList<RecurrenceAmountFrom> amounts, decimal fallback, string period, EstimatedAdjustment? estimate)
    {
        var known = For(amounts, fallback, period);
        if (estimate is null || string.CompareOrdinal(period, estimate.FromPeriod) < 0
            || amounts.Any(a => string.CompareOrdinal(a.FromPeriod, estimate.FromPeriod) > 0 && string.CompareOrdinal(a.FromPeriod, period) <= 0))
        {
            return new ForecastAmount(known, 0m);
        }

        return new ForecastAmount(estimate.Amount, Math.Abs(estimate.Amount - known));
    }
}
