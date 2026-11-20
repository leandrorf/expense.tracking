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
    public const int SupportedVersion = 1;
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

/// <summary>Payload v1 do <c>TransactionDeleted</c>.</summary>
public sealed record TransactionDeletedV1(Guid TransactionId, Guid OwnerUserId);

/// <summary>
/// Um evento de tipo conhecido chegou numa versao que o reporting nao sabe ler. O lote e
/// abortado sem avancar o checkpoint: pular o evento perderia dados em silencio.
/// </summary>
public sealed class UnsupportedEventVersionException(FeedEvent feedEvent)
    : Exception($"Evento {feedEvent.EventType} v{feedEvent.Version} (sequencia {feedEvent.Sequence}) nao suportado; o reporting le a v{FeedEventTypes.SupportedVersion}.")
{
    public FeedEvent FeedEvent { get; } = feedEvent;
}
