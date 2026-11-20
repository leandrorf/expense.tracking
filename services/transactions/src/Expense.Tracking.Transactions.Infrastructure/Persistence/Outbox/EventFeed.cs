using Expense.Tracking.Transactions.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Outbox;

internal sealed class EventFeed(TransactionsDbContext dbContext) : IEventFeed
{
    public async Task<IReadOnlyList<FeedEvent>> ReadAfterAsync(long afterSequence, int limit, CancellationToken cancellationToken) =>
        await dbContext.OutboxEvents
            .AsNoTracking()
            .Where(e => e.Sequence > afterSequence)
            .OrderBy(e => e.Sequence)
            .Take(limit)
            .Select(e => new FeedEvent(
                e.Sequence, e.EventId, e.EventType, e.Version, e.AggregateId, e.OwnerUserId,
                e.OccurredAtUtc, e.TraceParent, e.Payload))
            .ToListAsync(cancellationToken);

    public async Task<long> GetHeadSequenceAsync(CancellationToken cancellationToken) =>
        await dbContext.OutboxEvents.MaxAsync(e => (long?)e.Sequence, cancellationToken) ?? 0;
}
