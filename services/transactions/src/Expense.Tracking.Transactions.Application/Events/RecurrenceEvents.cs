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

/// <summary>Uma vigencia do valor no payload: valor a partir de um periodo.</summary>
public sealed record RecurrenceAmountPayload(string FromPeriod, decimal Amount);

/// <summary>
/// Uma regra foi criada ou corrigida: estado completo, com o escopo PF/PJ da conta (a conta nao muda).
/// <see cref="RecurrenceVersion"/> e a versao da regra, nao a <see cref="TransactionEvent.Version"/> do
/// formato do evento.
/// <para>
/// Versao 2 (RJT-2, ADR-0010, decisao 7): <see cref="Amount"/> passa a ser o valor vigente no dia do evento,
/// e <see cref="Amounts"/> traz todas as vigencias (a primeira inclusive). <see cref="Adjustment"/> (RJT-4)
/// e o reajuste anual configurado, nulo sem reajuste. Os eventos v1 ja gravados continuam no feed.
/// </para>
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
    int RecurrenceVersion,
    IReadOnlyList<RecurrenceAmountPayload> Amounts,
    object? Adjustment = null)
    : RecurrenceEvent(RecurrenceId, OwnerUserId)
{
    public const string Name = "RecurrenceSaved";

    public const int CurrentVersion = 2;

    [JsonIgnore]
    public override string EventType => Name;

    [JsonIgnore]
    public override int Version => CurrentVersion;

    /// <param name="currentPeriod">Periodo de hoje: o <c>amount</c> do payload e o valor vigente nele.</param>
    public static RecurrenceSaved From(Recurrence recurrence, string? accountScope, string currentPeriod) => new(
        recurrence.Id,
        recurrence.OwnerUserId,
        recurrence.AccountId,
        accountScope,
        recurrence.Type.ToString(),
        recurrence.PaymentMethod.ToString(),
        recurrence.CreditCardId,
        recurrence.Description,
        recurrence.Category,
        recurrence.AmountFor(currentPeriod),
        recurrence.Currency,
        recurrence.Frequency.ToString(),
        recurrence.StartsOn,
        recurrence.EndsOn,
        recurrence.Version,
        recurrence.Amounts.Select(a => new RecurrenceAmountPayload(a.FromPeriod, a.Amount)).ToList());
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
