using System.Globalization;
using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Domain.Recurrences;

/// <summary>REC (ADR-0008, decisao 3): de quanto em quanto tempo a regra se repete.</summary>
public enum RecurrenceFrequency
{
    Monthly = 0,
    Yearly = 1,
}

/// <summary>
/// REC-2 (ADR-0008): lancamento que se repete. A regra so projeta: nunca vira lancamento sozinha. Cada
/// ocorrencia e identificada pelo <b>periodo</b> (<c>yyyy-MM</c> na mensal, <c>yyyy</c> na anual), nao
/// pela data, e a agenda e ancorada em <see cref="StartsOn"/>: a mensal repete no dia dela todo mes, a
/// anual no dia e mes dela todo ano; mes sem aquele dia usa o ultimo dia.
/// </summary>
public sealed class Recurrence
{
    // RJT-2 (ADR-0010, decisao 1): vigencias depois da primeira. A primeira e o Amount da regra, no periodo do inicio.
    private readonly List<RecurrenceAmount> _laterAmounts = [];

    private Recurrence()
    {
        Description = string.Empty;
        Category = string.Empty;
        Currency = "BRL";
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public Guid AccountId { get; private set; }

    public TransactionType Type { get; private set; }

    public PaymentMethod PaymentMethod { get; private set; }

    public Guid? CreditCardId { get; private set; }

    public string Description { get; private set; }

    public string Category { get; private set; }

    /// <summary>
    /// Valor estimado da <b>primeira vigencia</b> (do periodo do inicio). O valor de um periodo e o de
    /// <see cref="AmountFor"/>: a ultima vigencia que comecou ate ele (RJT-2, ADR-0010, decisao 1).
    /// </summary>
    public decimal Amount { get; private set; }

    /// <summary>Vigencias depois da primeira, em ordem de periodo.</summary>
    public IReadOnlyList<RecurrenceAmount> LaterAmounts => _laterAmounts.OrderBy(a => a.FromPeriod, StringComparer.Ordinal).ToList();

    /// <summary>Periodo do inicio, onde comeca a primeira vigencia.</summary>
    public string FirstPeriod => PeriodOf(StartsOn);

    /// <summary>Todas as vigencias, a primeira inclusive, em ordem.</summary>
    public IReadOnlyList<RecurrenceAmount> Amounts =>
        [new RecurrenceAmount(FirstPeriod, Amount), .. LaterAmounts];

    /// <summary>Valor estimado de um periodo: o da ultima vigencia com inicio ate ele.</summary>
    public decimal AmountFor(string period) =>
        LaterAmounts.LastOrDefault(a => string.CompareOrdinal(a.FromPeriod, period) <= 0)?.Amount ?? Amount;

    public string Currency { get; private set; }

    public string? Notes { get; private set; }

    public RecurrenceFrequency Frequency { get; private set; }

    /// <summary>Primeira data da agenda e ancora dela (dia, e mes na anual).</summary>
    public DateOnly StartsOn { get; private set; }

    /// <summary>Ultima data possivel da agenda (inclusiva); nulo = sem fim.</summary>
    public DateOnly? EndsOn { get; private set; }

    /// <summary>Concorrencia otimista, como o lancamento (ADR-0007).</summary>
    public int Version { get; private set; } = 1;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Result<Recurrence> Create(
        Guid ownerUserId, Guid accountId, TransactionType type, PaymentMethod paymentMethod, Guid? creditCardId,
        string? description, string? category, decimal amount, string? currency, string? notes,
        RecurrenceFrequency frequency, DateOnly startsOn, DateOnly? endsOn, DateTimeOffset nowUtc)
    {
        var recurrence = new Recurrence
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            AccountId = accountId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        var applied = recurrence.Apply(type, paymentMethod, creditCardId, description, category, amount, currency, notes,
            frequency, startsOn, endsOn, nowUtc, dryRun: false);
        return applied.IsFailure ? Result.Failure<Recurrence>(applied.Error) : recurrence;
    }

    /// <summary>
    /// Corrige a regra com as regras da criacao. A conta nao muda. Devolve <c>true</c> quando algo mudou
    /// (a versao sobe); sem mudanca, nada e tocado. So os periodos em aberto sentem a mudanca: os
    /// fechados (lancados ou pulados) sao guardados a parte. RJT-2 (ADR-0010, decisao 2): o
    /// <paramref name="amount"/> corrige a vigencia que vale em <paramref name="currentPeriod"/> (hoje), sem
    /// criar vigencia; as vigencias seguintes precisam continuar na agenda nova.
    /// </summary>
    public Result<bool> Edit(
        TransactionType type, PaymentMethod paymentMethod, Guid? creditCardId, string? description, string? category,
        decimal amount, string? currency, string? notes, RecurrenceFrequency frequency, DateOnly startsOn,
        DateOnly? endsOn, DateTimeOffset nowUtc, string? currentPeriod = null)
    {
        var target = currentPeriod is null ? null : LaterAmounts.LastOrDefault(a => string.CompareOrdinal(a.FromPeriod, currentPeriod) <= 0);
        var baseAmount = target is null ? amount : Amount;
        var amountChanged = target is not null && target.Amount != amount;
        if (amountChanged)
        {
            var money = Money.Create(amount, currency);
            if (money.IsFailure)
            {
                return Result.Failure<bool>(money.Error);
            }
        }

        var applied = Apply(type, paymentMethod, creditCardId, description, category, baseAmount, currency, notes, frequency,
            startsOn, endsOn, nowUtc, dryRun: true, forceChanged: amountChanged);
        if (applied.IsFailure || !applied.Value)
        {
            return applied;
        }

        // Com outra agenda (inicio, fim ou frequencia), as vigencias seguintes precisam continuar nela.
        if (_laterAmounts.Any(a => ScheduledOn(a.FromPeriod) is null || string.CompareOrdinal(a.FromPeriod, FirstPeriod) <= 0))
        {
            return Result.Failure<bool>(Error.Validation(
                "recurrence.amounts_outside_schedule",
                "Ha valores com vigencia fora da nova agenda: remova-os antes de mudar o inicio, o fim ou a frequencia."));
        }

        if (amountChanged)
        {
            _laterAmounts.Remove(target!);
            _laterAmounts.Add(new RecurrenceAmount(target!.FromPeriod, amount));
        }

        return true;
    }

    /// <summary>
    /// RJT-2 (ADR-0010, decisao 1): o valor passa a <paramref name="amount"/> a partir de
    /// <paramref name="fromPeriod"/>, sem mudar os periodos anteriores. O periodo precisa ser da agenda,
    /// depois do primeiro e ainda nao fechado; se ja tem vigencia, o valor dela e trocado.
    /// </summary>
    public Result SetAmountFrom(string fromPeriod, decimal amount, IReadOnlySet<string> closedPeriods, DateTimeOffset nowUtc)
    {
        if (ScheduledOn(fromPeriod) is null || string.CompareOrdinal(fromPeriod, FirstPeriod) <= 0 || closedPeriods.Contains(fromPeriod))
        {
            return Result.Failure(Error.Validation(
                "recurrence.amount_period_invalid",
                "O valor novo precisa comecar num periodo da recorrencia, depois do primeiro e ainda nao lancado nem pulado."));
        }

        var money = Money.Create(amount, Currency);
        if (money.IsFailure)
        {
            return Result.Failure(money.Error);
        }

        _laterAmounts.RemoveAll(a => a.FromPeriod == fromPeriod);
        _laterAmounts.Add(new RecurrenceAmount(fromPeriod, money.Value.Amount));
        UpdatedAtUtc = nowUtc;
        Version++;
        return Result.Success();
    }

    /// <summary>RJT-2: tira uma vigencia que ainda nao comecou (depois de <paramref name="currentPeriod"/>).</summary>
    public Result RemoveAmountFrom(string fromPeriod, string currentPeriod, DateTimeOffset nowUtc)
    {
        if (_laterAmounts.FirstOrDefault(a => a.FromPeriod == fromPeriod) is not { } amount)
        {
            return Result.Failure(Error.NotFound("recurrence.amount_not_found", "Nao ha valor com vigencia nesse periodo."));
        }

        if (string.CompareOrdinal(fromPeriod, currentPeriod) <= 0)
        {
            return Result.Failure(Error.Validation(
                "recurrence.amount_already_started", "Esse valor ja esta valendo: para corrigi-lo, edite o valor da recorrencia."));
        }

        _laterAmounts.Remove(amount);
        UpdatedAtUtc = nowUtc;
        Version++;
        return Result.Success();
    }

    private Result<bool> Apply(
        TransactionType type, PaymentMethod paymentMethod, Guid? creditCardId, string? description, string? category,
        decimal amount, string? currency, string? notes, RecurrenceFrequency frequency, DateOnly startsOn,
        DateOnly? endsOn, DateTimeOffset nowUtc, bool dryRun, bool forceChanged = false)
    {
        if (!Enum.IsDefined(frequency))
        {
            return Result.Failure<bool>(Error.Validation("recurrence.frequency_invalid", "Frequencia invalida: use 'Monthly' ou 'Yearly'."));
        }

        if (endsOn is { } end && end < startsOn)
        {
            return Result.Failure<bool>(Error.Validation("recurrence.ends_before_start", "O fim da recorrencia nao pode ser antes do inicio."));
        }

        // Mesmas regras e codigos do lancamento (ADR-0008, decisao 5); a regra nao tem parcelas.
        var valid = Transaction.Create(OwnerUserId, AccountId, type, paymentMethod, creditCardId, description, category,
            amount, currency, startsOn, notes, nowUtc, installments: 1);
        if (valid.IsFailure)
        {
            return Result.Failure<bool>(valid.Error);
        }

        var next = valid.Value;
        var changed = Type != next.Type || PaymentMethod != next.PaymentMethod || CreditCardId != next.CreditCardId
            || Description != next.Description || Category != next.Category || Amount != next.Amount
            || Currency != next.Currency || Notes != next.Notes || Frequency != frequency || StartsOn != startsOn
            || EndsOn != endsOn || forceChanged;
        if (dryRun && !changed)
        {
            return false;
        }

        Type = next.Type;
        PaymentMethod = next.PaymentMethod;
        CreditCardId = next.CreditCardId;
        Description = next.Description;
        Category = next.Category;
        Amount = next.Amount;
        Currency = next.Currency;
        Notes = next.Notes;
        Frequency = frequency;
        StartsOn = startsOn;
        EndsOn = endsOn;
        if (dryRun)
        {
            UpdatedAtUtc = nowUtc;
            Version++;
        }

        return true;
    }

    // ---------- Agenda (ADR-0008, decisoes 3 e 4) ----------

    /// <summary>Periodo de uma data nesta frequencia: <c>yyyy-MM</c> (mensal) ou <c>yyyy</c> (anual).</summary>
    public string PeriodOf(DateOnly date) => RecurrenceSchedule.PeriodOf(Frequency, date);

    /// <summary>Data agendada de um periodo, ou nulo se o periodo e invalido ou esta fora da agenda.</summary>
    public DateOnly? ScheduledOn(string period) => RecurrenceSchedule.ScheduledOn(Frequency, StartsOn, EndsOn, period);

    /// <summary>Ocorrencias da agenda com data entre <paramref name="from"/> e <paramref name="to"/> (inclusive).</summary>
    public IEnumerable<ScheduledOccurrence> Occurrences(DateOnly from, DateOnly to) =>
        RecurrenceSchedule.Occurrences(Frequency, StartsOn, EndsOn, from, to);
}

/// <summary>Uma ocorrencia da agenda: o periodo que a identifica e a data em que cai.</summary>
public readonly record struct ScheduledOccurrence(string Period, DateOnly ScheduledOn);

/// <summary>Regra pura da agenda de uma recorrencia (ADR-0008, decisoes 3 e 4).</summary>
public static class RecurrenceSchedule
{
    public static string PeriodOf(RecurrenceFrequency frequency, DateOnly date) => frequency == RecurrenceFrequency.Yearly
        ? date.Year.ToString("0000", CultureInfo.InvariantCulture)
        : date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static DateOnly? ScheduledOn(RecurrenceFrequency frequency, DateOnly startsOn, DateOnly? endsOn, string period)
    {
        DateOnly date;
        if (frequency == RecurrenceFrequency.Yearly)
        {
            if (period.Length != 4 || !int.TryParse(period, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                || year is < 1 or > 9999)
            {
                return null;
            }

            date = Clamp(year, startsOn.Month, startsOn.Day);
        }
        else
        {
            if (!DateOnly.TryParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first))
            {
                return null;
            }

            date = Clamp(first.Year, first.Month, startsOn.Day);
        }

        return date < startsOn || (endsOn is { } end && date > end) ? null : date;
    }

    public static IEnumerable<ScheduledOccurrence> Occurrences(
        RecurrenceFrequency frequency, DateOnly startsOn, DateOnly? endsOn, DateOnly from, DateOnly to)
    {
        var start = from < startsOn ? startsOn : from;
        var last = endsOn is { } end && end < to ? end : to;
        if (start > last)
        {
            yield break;
        }

        var cursor = frequency == RecurrenceFrequency.Yearly ? new DateOnly(start.Year, 1, 1) : new DateOnly(start.Year, start.Month, 1);
        while (cursor <= last)
        {
            var period = PeriodOf(frequency, cursor);
            if (ScheduledOn(frequency, startsOn, endsOn, period) is { } date && date >= start && date <= last)
            {
                yield return new ScheduledOccurrence(period, date);
            }

            cursor = frequency == RecurrenceFrequency.Yearly ? cursor.AddYears(1) : cursor.AddMonths(1);
        }
    }

    private static DateOnly Clamp(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
}

/// <summary>RJT-2 (ADR-0010, decisao 1): o valor estimado da recorrencia a partir de um periodo.</summary>
public sealed record RecurrenceAmount(string FromPeriod, decimal Amount);
