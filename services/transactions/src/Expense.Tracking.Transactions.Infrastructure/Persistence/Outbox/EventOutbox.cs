using System.Diagnostics;
using System.Text.Json;
using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Events;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Outbox;

/// <summary>
/// Adiciona o evento ao mesmo <see cref="TransactionsDbContext"/> do caso de uso: ele e gravado no
/// <c>SaveChanges</c> do <see cref="IUnitOfWork"/>, na mesma transacao de banco da mudanca.
/// </summary>
internal sealed class EventOutbox(TransactionsDbContext dbContext, TimeProvider timeProvider) : IEventOutbox
{
    public void Add(TransactionEvent @event) =>
        dbContext.OutboxEvents.Add(ToOutboxEvent(@event, timeProvider.GetUtcNow(), CurrentTraceParent()));

    internal static OutboxEvent ToOutboxEvent(TransactionEvent @event, DateTimeOffset occurredAtUtc, string? traceParent) => new()
    {
        EventId = Guid.NewGuid(),
        EventType = @event.EventType,
        Version = @event.Version,
        AggregateId = @event.TransactionId,
        OwnerUserId = @event.OwnerUserId,
        OccurredAtUtc = occurredAtUtc,
        TraceParent = traceParent,
        // Tipo em runtime: serializa as propriedades do evento concreto, nao so as da base.
        Payload = JsonSerializer.Serialize(@event, @event.GetType(), EventJson.Options),
    };

    private static string? CurrentTraceParent() =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;
}

/// <summary>Formato JSON dos payloads de evento (camelCase, como as APIs).</summary>
internal static class EventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
