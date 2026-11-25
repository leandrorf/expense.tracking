using System.Globalization;
using Expense.Tracking.Transactions.Domain.Common;

namespace Expense.Tracking.Transactions.Domain.Recurrences;

/// <summary>RJT-4 (ADR-0010, decisao 3): como o valor e reajustado no aniversario.</summary>
public enum RecurrenceAdjustmentKind
{
    /// <summary>Pelo acumulado de 12 meses de um indice oficial.</summary>
    Index = 0,

    /// <summary>Por um percentual combinado.</summary>
    Fixed = 1,
}

/// <summary>RJT-4: indices oficiais aceitos (series do SGS do Banco Central: 433, 188 e 189).</summary>
public enum EconomicIndex
{
    IPCA = 0,
    INPC = 1,
    IGPM = 2,
}

/// <summary>O reajuste como o cliente o manda: o mes do aniversario e opcional (padrao: o mes do inicio).</summary>
public sealed record RecurrenceAdjustmentSpec(
    int? AnniversaryMonth, RecurrenceAdjustmentKind Kind, EconomicIndex? Index, decimal? Rate, bool NoReduction = true);

/// <summary>
/// RJT-4 (ADR-0010, decisao 3): reajuste anual configurado na regra. <see cref="Rate"/> so no
/// <see cref="RecurrenceAdjustmentKind.Fixed"/>; <see cref="Index"/> so no <see cref="RecurrenceAdjustmentKind.Index"/>.
/// </summary>
public sealed record RecurrenceAdjustment(
    int AnniversaryMonth, RecurrenceAdjustmentKind Kind, EconomicIndex? Index, decimal? Rate, bool NoReduction)
{
    /// <summary>Maior percentual aceito (e menor que -100%, que zeraria o valor).</summary>
    public const decimal MaxRate = 1000m;

    /// <summary>Valida o pedido contra a agenda da regra; na anual, o aniversario e o proprio mes dela.</summary>
    public static Result<RecurrenceAdjustment?> Normalize(RecurrenceAdjustmentSpec? spec, RecurrenceFrequency frequency, DateOnly startsOn)
    {
        if (spec is null)
        {
            return Result.Success<RecurrenceAdjustment?>(null);
        }

        if (!Enum.IsDefined(spec.Kind))
        {
            return Result.Failure<RecurrenceAdjustment?>(Error.Validation(
                "recurrence.adjustment_kind_invalid", "Tipo de reajuste invalido: use 'Index' ou 'Fixed'."));
        }

        var month = spec.AnniversaryMonth ?? startsOn.Month;
        if (month is < 1 or > 12 || (frequency == RecurrenceFrequency.Yearly && month != startsOn.Month))
        {
            return Result.Failure<RecurrenceAdjustment?>(Error.Validation(
                "recurrence.adjustment_month_invalid",
                "O mes do reajuste vai de 1 a 12; na recorrencia anual, e o proprio mes dela."));
        }

        if (spec.Kind == RecurrenceAdjustmentKind.Index)
        {
            if (spec.Index is not { } index || !Enum.IsDefined(index))
            {
                return Result.Failure<RecurrenceAdjustment?>(Error.Validation(
                    "recurrence.adjustment_index_invalid", "Informe o indice do reajuste: IPCA, INPC ou IGPM."));
            }

            return new RecurrenceAdjustment(month, spec.Kind, index, null, spec.NoReduction);
        }

        var rate = ValidateRate(spec.Rate);
        return rate.IsFailure
            ? Result.Failure<RecurrenceAdjustment?>(rate.Error)
            : new RecurrenceAdjustment(month, spec.Kind, null, rate.Value, spec.NoReduction);
    }

    /// <summary>Percentual acima de -100% e ate <see cref="MaxRate"/>, com ate 4 casas.</summary>
    public static Result<decimal> ValidateRate(decimal? rate) =>
        rate is { } r && r > -100m && r <= MaxRate && decimal.Round(r, 4) == r
            ? r
            : Result.Failure<decimal>(Error.Validation(
                "recurrence.adjustment_rate_invalid", "Informe o percentual do reajuste (acima de -100%, ate 4 casas)."));

    /// <summary>
    /// ADR-0010, decisao 4: valor reajustado, a centavos com meio para cima; com "nao reduzir", nunca
    /// abaixo do anterior.
    /// </summary>
    public decimal Apply(decimal previous, decimal rate)
    {
        var adjusted = decimal.Round(previous * (1m + rate / 100m), 2, MidpointRounding.AwayFromZero);
        return NoReduction && adjusted < previous ? previous : adjusted;
    }

    /// <summary>Periodo do aniversario de um ano: <c>yyyy-MM</c> na mensal, <c>yyyy</c> na anual.</summary>
    public string PeriodOf(int year, RecurrenceFrequency frequency) => frequency == RecurrenceFrequency.Yearly
        ? year.ToString("0000", CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"{year:0000}-{AnniversaryMonth:00}");
}

/// <summary>RJT-4 (ADR-0010, decisao 4): um aniversario ja aplicado (nao se aplica duas vezes).</summary>
public sealed record RecurrenceAdjustmentApplied(
    int Year, string FromPeriod, decimal PreviousAmount, decimal NewAmount, decimal? Rate, DateTimeOffset AppliedAtUtc);

/// <summary>
/// RJT-4 (ADR-0010, decisao 5): reajuste do aniversario mais recente ainda nao aplicado. <see cref="Rate"/> e
/// <see cref="SuggestedAmount"/> vem quando da para calcular (percentual fixo; o indice chega na RJT-7).
/// </summary>
public sealed record RecurrencePendingAdjustment(int Year, string Period, decimal CurrentAmount, decimal? Rate, decimal? SuggestedAmount);
