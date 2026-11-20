namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Outbox;

/// <summary>
/// EVT-1: linha do outbox. A tabela e um <b>log de eventos retido</b> (ADR-0001): nada e apagado
/// depois de lido, para que consumidores possam reconstruir projecoes desde a sequencia 0.
/// </summary>
public sealed class OutboxEvent
{
    /// <summary>Ordem global do feed (auto-incremento). E o cursor dos consumidores.</summary>
    public long Sequence { get; set; }

    /// <summary>Id unico do evento, para idempotencia nos consumidores.</summary>
    public Guid EventId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public int Version { get; set; }

    /// <summary>Lancamento a que o evento se refere.</summary>
    public Guid AggregateId { get; set; }

    public Guid OwnerUserId { get; set; }

    /// <summary>Quando o evento foi gravado. Base da margem de seguranca do feed.</summary>
    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>Contexto W3C da request de origem, para ligar o processamento ao trace original.</summary>
    public string? TraceParent { get; set; }

    /// <summary>Payload do evento em JSON (camelCase).</summary>
    public string Payload { get; set; } = string.Empty;
}
