namespace Expense.Tracking.Reporting.Domain.Forecast;

/// <summary>RJT-3 (ADR-0010, decisao 1): o valor estimado de uma regra a partir de um periodo.</summary>
public sealed record RecurrenceAmountFrom(string FromPeriod, decimal Amount);

/// <summary>
/// RJT-8 (ADR-0010, decisao 8): reajuste anual da regra com a taxa estimada (%); nula = sem estimativa (o
/// indice ainda nao tem os 12 meses). <see cref="Index"/> (RJT-9) e nulo no percentual fixo.
/// </summary>
public sealed record RecurrenceAdjustmentEstimate(int AnniversaryMonth, decimal? EstimatedRate, bool NoReduction, string? Index = null)
{
    /// <summary>Valor reajustado pela taxa estimada, a centavos (meio para cima), com o "nao reduzir"; nulo sem taxa.</summary>
    public decimal? Adjust(decimal amount)
    {
        if (EstimatedRate is not { } rate)
        {
            return null;
        }

        var adjusted = decimal.Round(amount * (1m + rate / 100m), 2, MidpointRounding.AwayFromZero);
        return NoReduction && adjusted < amount ? amount : adjusted;
    }
}

/// <summary>RJT-9: aniversario ja chegado e ainda sem vigencia (o reajuste esta pendente), com o valor antes dele.</summary>
public sealed record PendingAnniversary(int Year, string Period, DateOnly On, decimal Base);

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
        if (adjustment?.EstimatedRate is null)
        {
            return null;
        }

        var recent = today.Month >= adjustment.AnniversaryMonth ? today.Year : today.Year - 1;
        foreach (var year in new[] { recent, recent + 1 })
        {
            if (Anniversary(amounts, fallback, frequency, startsOn, null, year, adjustment.AnniversaryMonth) is { } at)
            {
                return new EstimatedAdjustment(at.Period, at.Base, adjustment.Adjust(at.Base)!.Value);
            }
        }

        return null;
    }

    /// <summary>
    /// RJT-9 (ADR-0010, decisao 10): o aniversario mais recente (do mes dele ate 11 meses depois) que ainda
    /// nao virou vigencia, dentro da regra e depois do periodo do inicio; nulo se nao ha reajuste pendente.
    /// </summary>
    public static PendingAnniversary? Pending(
        IReadOnlyList<RecurrenceAmountFrom> amounts, decimal fallback, string frequency, DateOnly startsOn, DateOnly? endsOn,
        DateOnly today, int anniversaryMonth) =>
        Anniversary(amounts, fallback, frequency, startsOn, endsOn, today.Month >= anniversaryMonth ? today.Year : today.Year - 1, anniversaryMonth);

    private static PendingAnniversary? Anniversary(
        IReadOnlyList<RecurrenceAmountFrom> amounts, decimal fallback, string frequency, DateOnly startsOn, DateOnly? endsOn,
        int year, int anniversaryMonth)
    {
        var on = new DateOnly(year, anniversaryMonth, 1);
        var period = RecurrenceSchedule.PeriodOf(frequency, on);
        if (string.CompareOrdinal(period, RecurrenceSchedule.PeriodOf(frequency, startsOn)) <= 0
            || (endsOn is { } end && string.CompareOrdinal(period, RecurrenceSchedule.PeriodOf(frequency, end)) > 0)
            || amounts.Any(a => a.FromPeriod == period))
        {
            return null;
        }

        var before = amounts.Where(a => string.CompareOrdinal(a.FromPeriod, period) < 0)
            .OrderBy(a => a.FromPeriod, StringComparer.Ordinal).LastOrDefault()?.Amount ?? fallback;
        return new PendingAnniversary(year, period, on, before);
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
