using System.Text.Json;

namespace Expense.Tracking.Reporting.Application.Projection;

/// <summary>
/// Contrato do feed <c>GET /events</c> do transactions, do lado consumidor (ADR-0001). E uma
/// copia propria: o reporting nao depende de codigo do transactions, so do formato publicado.
/// </summary>
public sealed record FeedEvent(
    long Sequence,
    Guid EventId,
    string EventType,
    int Version,
    Guid AggregateId,
    Guid OwnerUserId,
    DateTimeOffset OccurredAtUtc,
    string? TraceParent,
    JsonElement Payload);

/// <summary>
/// Pagina do feed; a proxima leitura usa <see cref="LastSequence"/> como cursor.
/// <see cref="HeadSequence"/> e a maior sequencia gravada no outbox (EVT-3, atraso da projecao);
/// vem 0 de um transactions anterior ao campo.
/// </summary>
public sealed record FeedPage(IReadOnlyList<FeedEvent> Events, long LastSequence, long HeadSequence = 0);

/// <summary>Nomes e versoes de evento que o reporting sabe aplicar.</summary>
public static class FeedEventTypes
{
    public const string TransactionRegistered = "TransactionRegistered";
    public const string TransactionDeleted = "TransactionDeleted";
    public const string TransactionAccountScopeResolved = "TransactionAccountScopeResolved";
    public const string TransactionCardDetailsResolved = "TransactionCardDetailsResolved";
    public const string TransactionUpdated = "TransactionUpdated";
    public const string CardInvoicePaid = "CardInvoicePaid";
    public const string CardInvoicePaymentDeleted = "CardInvoicePaymentDeleted";
    public const string RecurrenceSaved = "RecurrenceSaved";
    public const string RecurrenceDeleted = "RecurrenceDeleted";
    public const string RecurrenceOccurrenceClosed = "RecurrenceOccurrenceClosed";
    public const string RecurrenceOccurrenceReopened = "RecurrenceOccurrenceReopened";

    /// <summary>Versao mais nova que o reporting le de cada tipo; as anteriores continuam valendo.</summary>
    public static int SupportedVersionOf(string eventType) => eventType is TransactionRegistered or RecurrenceSaved ? 2 : 1;
}

/// <summary>Payload v1 do <c>TransactionRegistered</c>.</summary>
public sealed record TransactionRegisteredV1(
    Guid TransactionId,
    Guid OwnerUserId,
    Guid AccountId,
    string? AccountScope,
    string TransactionType,
    string PaymentMethod,
    string Category,
    decimal Amount,
    string Currency,
    DateOnly OccurredOn);

/// <summary>
/// Payload v2 do <c>TransactionRegistered</c> (INV-2, ADR-0005): v1 mais descricao, cartao (nulo
/// fora do cartao) e parcelas.
/// </summary>
public sealed record TransactionRegisteredV2(
    Guid TransactionId,
    Guid OwnerUserId,
    Guid AccountId,
    string? AccountScope,
    string TransactionType,
    string PaymentMethod,
    string Category,
    decimal Amount,
    string Currency,
    DateOnly OccurredOn,
    string? Description,
    Guid? CreditCardId,
    int Installments);

/// <summary>
/// Payload v1 do <c>TransactionCardDetailsResolved</c> (INV-2): descricao, cartao e parcelas de uma
/// compra no cartao cujo <c>TransactionRegistered</c> saiu na v1.
/// </summary>
public sealed record TransactionCardDetailsResolvedV1(
    Guid TransactionId, Guid OwnerUserId, string Description, Guid CreditCardId, int Installments);

/// <summary>
/// Payload v1 do <c>TransactionAccountScopeResolved</c> (EVT-4): escopo descoberto depois para um
/// lancamento cujo <c>TransactionRegistered</c> saiu sem escopo.
/// </summary>
public sealed record TransactionAccountScopeResolvedV1(Guid TransactionId, Guid OwnerUserId, Guid AccountId, string AccountScope);

/// <summary>
/// Payload v1 do <c>CardInvoicePaid</c> (INV-6, ADR-0005): pagamento da fatura do mes de vencimento
/// <see cref="InvoiceDueMonth"/> (<c>yyyy-MM</c>). Nao e lancamento.
/// </summary>
public sealed record CardInvoicePaidV1(
    Guid PaymentId,
    Guid OwnerUserId,
    Guid CreditCardId,
    string InvoiceDueMonth,
    Guid AccountId,
    decimal Amount,
    string Currency,
    DateOnly PaidOn);

/// <summary>Payload v1 do <c>CardInvoicePaymentDeleted</c> (INV-6).</summary>
public sealed record CardInvoicePaymentDeletedV1(Guid PaymentId, Guid OwnerUserId, Guid CreditCardId);

/// <summary>
/// Payload v1 do <c>TransactionUpdated</c> (LNC-3, ADR-0007): o estado completo do lancamento depois
/// da edicao. Sem escopo, porque a conta nao muda; <see cref="TransactionVersion"/> e a versao do
/// lancamento, nao a do formato do evento.
/// </summary>
public sealed record TransactionUpdatedV1(
    Guid TransactionId,
    Guid OwnerUserId,
    Guid AccountId,
    string TransactionType,
    string PaymentMethod,
    string Category,
    decimal Amount,
    string Currency,
    DateOnly OccurredOn,
    string Description,
    Guid? CreditCardId,
    int Installments,
    int TransactionVersion);

/// <summary>
/// Payload v1 do <c>RecurrenceSaved</c> (REC-2, ADR-0008): estado completo da regra, com o escopo da
/// conta; <see cref="RecurrenceVersion"/> e a versao da regra, nao a do formato do evento.
/// </summary>
public sealed record RecurrenceSavedV1(
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
    int RecurrenceVersion);

/// <summary>Uma vigencia no payload do <c>RecurrenceSaved</c> v2.</summary>
public sealed record RecurrenceAmountV2(string FromPeriod, decimal Amount);

/// <summary>
/// RJT-8: reajuste anual no payload do <c>RecurrenceSaved</c> v2. <c>estimatedRate</c> e a taxa que a previsao
/// usa para o aniversario ainda nao aplicado (nula = sem estimativa).
/// </summary>
public sealed record RecurrenceAdjustmentV2(
    int AnniversaryMonth, string Kind, string? Index, decimal? Rate, bool NoReduction, decimal? EstimatedRate);

/// <summary>
/// Payload v2 do <c>RecurrenceSaved</c> (RJT-2, ADR-0010, decisao 7): os campos da v1 (com <c>amount</c> =
/// valor vigente no dia do evento) mais todas as vigencias. O reajuste configurado (RJT-4) vem em
/// <c>adjustment</c>, lido pela RJT-8.
/// </summary>
public sealed record RecurrenceSavedV2(
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
    IReadOnlyList<RecurrenceAmountV2>? Amounts,
    RecurrenceAdjustmentV2? Adjustment = null);

/// <summary>Payload v1 do <c>RecurrenceDeleted</c>.</summary>
public sealed record RecurrenceDeletedV1(Guid RecurrenceId, Guid OwnerUserId);

/// <summary>Payload v1 do <c>RecurrenceOccurrenceClosed</c> (REC-3): <c>Launched</c> (com o lancamento) ou <c>Skipped</c>.</summary>
public sealed record RecurrenceOccurrenceClosedV1(Guid RecurrenceId, Guid OwnerUserId, string Period, string Status, Guid? TransactionId);

/// <summary>Payload v1 do <c>RecurrenceOccurrenceReopened</c> (REC-3).</summary>
public sealed record RecurrenceOccurrenceReopenedV1(Guid RecurrenceId, Guid OwnerUserId, string Period);

/// <summary>Payload v1 do <c>TransactionDeleted</c>.</summary>
public sealed record TransactionDeletedV1(Guid TransactionId, Guid OwnerUserId);

/// <summary>
/// Um evento de tipo conhecido chegou numa versao que o reporting nao sabe ler. O lote e
/// abortado sem avancar o checkpoint: pular o evento perderia dados em silencio.
/// </summary>
public sealed class UnsupportedEventVersionException(FeedEvent feedEvent)
    : Exception($"Evento {feedEvent.EventType} v{feedEvent.Version} (sequencia {feedEvent.Sequence}) nao suportado; o reporting le ate a v{FeedEventTypes.SupportedVersionOf(feedEvent.EventType)}.")
{
    public FeedEvent FeedEvent { get; } = feedEvent;
}
