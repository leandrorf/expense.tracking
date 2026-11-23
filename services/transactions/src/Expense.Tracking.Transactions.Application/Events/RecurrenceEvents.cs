using System.Text.Json.Serialization;
using Expense.Tracking.Transactions.Domain.Recurrences;

namespace Expense.Tracking.Transactions.Application.Events;

/// <summary>
/// REC (ADR-0008, decisao 2): base dos eventos de recorrencia. O agregado do outbox e a regra
/// (<see cref="RecurrenceId"/>), nao um lancamento: o <c>transactionId</c> da base fica fora do payload,
/// como nos eventos de pagamento de fatura.
/// </summary>
public abstract record RecurrenceEvent(Guid RecurrenceId, Guid OwnerUserId)
    : TransactionEvent(RecurrenceId, OwnerUserId)
{
    [JsonIgnore]
    public override Guid TransactionId
    {
        get => base.TransactionId;
        init => base.TransactionId = value;
    }
}

/// <summary>
/// Uma regra foi criada ou corrigida: estado completo, com o escopo PF/PJ da conta (a conta nao muda).
/// <see cref="RecurrenceVersion"/> e a versao da regra, nao a <see cref="TransactionEvent.Version"/> do
/// formato do evento.
/// </summary>
public sealed record RecurrenceSaved(
    Guid RecurrenceId,
    Guid OwnerUserId,
    Guid AccountId,
    string? AccountScope,
    string TransactionType,
    string PaymentMethod,
    Guid? CreditCardId,
    string Description,
    string Category,
    decimal Amount,
    string Currency,
    string Frequency,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    int RecurrenceVersion)
    : RecurrenceEvent(RecurrenceId, OwnerUserId)
{
    public const string Name = "RecurrenceSaved";

    [JsonIgnore]
    public override string EventType => Name;

    public static RecurrenceSaved From(Recurrence recurrence, string? accountScope) => new(
        recurrence.Id,
        recurrence.OwnerUserId,
        recurrence.AccountId,
        accountScope,
        recurrence.Type.ToString(),
        recurrence.PaymentMethod.ToString(),
        recurrence.CreditCardId,
        recurrence.Description,
        recurrence.Category,
        recurrence.Amount,
        recurrence.Currency,
        recurrence.Frequency.ToString(),
        recurrence.StartsOn,
        recurrence.EndsOn,
        recurrence.Version);
}

/// <summary>Uma regra foi excluida: as ocorrencias em aberto saem da previsao; os lancamentos ficam.</summary>
public sealed record RecurrenceDeleted(Guid RecurrenceId, Guid OwnerUserId)
    : RecurrenceEvent(RecurrenceId, OwnerUserId)
{
    public const string Name = "RecurrenceDeleted";

    [JsonIgnore]
    public override string EventType => Name;
}

/// <summary>
/// REC-3: o periodo de uma regra foi fechado: <c>Launched</c> (virou o lancamento
/// <see cref="TransactionIdOfLaunch"/>) ou <c>Skipped</c>. Sai da previsao como recorrente.
/// </summary>
public sealed record RecurrenceOccurrenceClosed(
    Guid RecurrenceId,
    Guid OwnerUserId,
    string Period,
    string Status,
    [property: JsonPropertyName("transactionId")] Guid? TransactionIdOfLaunch)
    : RecurrenceEvent(RecurrenceId, OwnerUserId)
{
    public const string Name = "RecurrenceOccurrenceClosed";

    [JsonIgnore]
    public override string EventType => Name;
}

/// <summary>REC-3: o periodo voltou a ficar em aberto (pulo desfeito ou lancamento excluido).</summary>
public sealed record RecurrenceOccurrenceReopened(Guid RecurrenceId, Guid OwnerUserId, string Period)
    : RecurrenceEvent(RecurrenceId, OwnerUserId)
{
    public const string Name = "RecurrenceOccurrenceReopened";

    [JsonIgnore]
    public override string EventType => Name;
}
