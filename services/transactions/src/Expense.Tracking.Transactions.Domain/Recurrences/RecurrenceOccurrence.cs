namespace Expense.Tracking.Transactions.Domain.Recurrences;

/// <summary>Como um periodo foi fechado (ADR-0008, decisao 7).</summary>
public enum OccurrenceStatus
{
    Launched = 0,
    Skipped = 1,
}

/// <summary>
/// REC-3 (ADR-0008, decisao 4): um periodo fechado de uma regra. So os fechados sao gravados; os em
/// aberto saem da agenda. Chave unica (regra, periodo): um periodo fecha uma vez so.
/// </summary>
public sealed class RecurrenceOccurrence
{
    private RecurrenceOccurrence()
    {
        Period = string.Empty;
    }

    public Guid RecurrenceId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    /// <summary><c>yyyy-MM</c> (mensal) ou <c>yyyy</c> (anual).</summary>
    public string Period { get; private set; }

    public OccurrenceStatus Status { get; private set; }

    /// <summary>O lancamento criado, quando <see cref="OccurrenceStatus.Launched"/>.</summary>
    public Guid? TransactionId { get; private set; }

    public DateTimeOffset ClosedAtUtc { get; private set; }

    public static RecurrenceOccurrence Launched(Recurrence recurrence, string period, Guid transactionId, DateTimeOffset nowUtc) =>
        new()
        {
            RecurrenceId = recurrence.Id,
            OwnerUserId = recurrence.OwnerUserId,
            Period = period,
            Status = OccurrenceStatus.Launched,
            TransactionId = transactionId,
            ClosedAtUtc = nowUtc,
        };

    public static RecurrenceOccurrence Skipped(Recurrence recurrence, string period, DateTimeOffset nowUtc) =>
        new()
        {
            RecurrenceId = recurrence.Id,
            OwnerUserId = recurrence.OwnerUserId,
            Period = period,
            Status = OccurrenceStatus.Skipped,
            ClosedAtUtc = nowUtc,
        };
}
