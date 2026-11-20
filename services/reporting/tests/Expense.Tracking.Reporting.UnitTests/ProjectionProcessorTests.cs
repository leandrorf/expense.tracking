using System.Diagnostics;
using System.Text.Json;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Projection;
using Expense.Tracking.Reporting.Domain.Projection;

namespace Expense.Tracking.Reporting.UnitTests;

/// <summary>EVT-2: regras de aplicacao do feed na projecao (idempotencia, checkpoint, versoes).</summary>
public sealed class ProjectionProcessorTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Batch_AppliesEvents_AndSavesProjectionAndCheckpointInOneCommit()
    {
        var feed = new Feed();
        var store = new Store();
        var tx = Guid.NewGuid();
        feed.Registered(tx, scope: "Business", amount: 120m);

        var result = await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        Assert.Equal(new ProjectionBatchResult(1, 0, 1, 1), result);
        var row = Assert.Single(store.Rows.Values);
        Assert.Equal("Business", row.AccountScope);
        Assert.Equal(120m, row.Amount);
        Assert.Equal(1, row.LastEventSequence);
        Assert.Equal(1, store.Saves);
        Assert.Equal(1, store.CheckpointAtSave);
    }

    [Fact]
    public async Task Deletion_RemovesTheRow_AndAnUnknownDeletionIsANoOp()
    {
        var feed = new Feed();
        var store = new Store();
        var tx = Guid.NewGuid();
        feed.Registered(tx);
        feed.Deleted(tx);
        feed.Deleted(Guid.NewGuid());

        var result = await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        Assert.Equal(3, result.Applied);
        Assert.Empty(store.Rows);
        Assert.Equal(3, store.Checkpoint);
    }

    [Fact]
    public async Task TheSameEventDeliveredAgain_DoesNotDuplicateValues()
    {
        var feed = new Feed();
        var store = new Store();
        var registered = feed.Registered(Guid.NewGuid(), amount: 50m);
        feed.Redeliver(registered);

        await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);
        store.Checkpoint = 0; // reler tudo de novo (reconstrucao)
        await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        var row = Assert.Single(store.Rows.Values);
        Assert.Equal(50m, row.Amount);
    }

    [Fact]
    public async Task UnknownEventType_IsSkipped_AndTheCheckpointStillAdvances()
    {
        var feed = new Feed();
        var store = new Store();
        feed.Raw("AccountRenamed", version: 1, new { foo = "bar" });

        var result = await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        Assert.Equal(new ProjectionBatchResult(0, 1, 1, 1), result);
        Assert.Empty(store.Rows);
        Assert.Equal(1, store.Checkpoint);
    }

    [Fact]
    public async Task UnsupportedVersion_AbortsTheBatch_WithoutSavingAnything()
    {
        var feed = new Feed();
        var store = new Store();
        feed.Registered(Guid.NewGuid());
        feed.Raw(FeedEventTypes.TransactionRegistered, version: 2, new { transactionId = Guid.NewGuid() });

        await Assert.ThrowsAsync<UnsupportedEventVersionException>(
            () => Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None));

        Assert.Equal(0, store.Saves);
        Assert.Equal(0, store.Checkpoint);
    }

    [Fact]
    public async Task EmptyFeed_DoesNotTouchTheStore()
    {
        var store = new Store { Checkpoint = 7 };

        var result = await Processor(new Feed(), store).ProcessNextBatchAsync(CancellationToken.None);

        Assert.Equal(new ProjectionBatchResult(0, 0, 7, 7), result);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Lag_IsTheHeadOfTheFeedMinusTheCheckpoint_EvenWithNothingVisible()
    {
        // Eventos ainda na margem do feed nao vem na pagina, mas ja contam no head.
        var feed = new Feed { HeadSequence = 12 };
        var store = new Store { Checkpoint = 9 };

        var result = await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        Assert.Equal(0, result.Read);
        Assert.Equal(3, result.Lag);
    }

    [Fact]
    public async Task Lag_IsZero_WhenTheFeedDoesNotSendTheHead()
    {
        var feed = new Feed { HeadSequence = 0 };
        feed.Registered(Guid.NewGuid());

        var result = await Processor(feed, new Store()).ProcessNextBatchAsync(CancellationToken.None);

        Assert.Equal((1L, 0L), (result.HeadSequence, result.Lag));
    }

    [Fact]
    public async Task Batch_IsReadFromTheCheckpoint_InPagesOfTheConfiguredSize()
    {
        var feed = new Feed();
        var store = new Store();
        for (var i = 0; i < 5; i++)
        {
            feed.Registered(Guid.NewGuid());
        }

        var processor = new ProjectionProcessor(feed, store, new ProjectionOptions(2, TimeSpan.Zero, true));
        var first = await processor.ProcessNextBatchAsync(CancellationToken.None);
        var second = await processor.ProcessNextBatchAsync(CancellationToken.None);

        Assert.Equal((2, 2L, 3L), (first.Read, first.Checkpoint, first.Lag));
        Assert.Equal((2, 4L, 1L), (second.Read, second.Checkpoint, second.Lag));
        Assert.Equal(4, store.Rows.Count);
    }

    [Fact]
    public async Task BatchSpan_LinksToTheTracesThatRecordedTheEvents()
    {
        var feed = new Feed();
        var origin = ActivityContext.Parse("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01", null);
        feed.Registered(Guid.NewGuid(), traceParent: "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");
        feed.Registered(Guid.NewGuid()); // evento da carga inicial: sem traceparent

        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ProjectionTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = captured.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await Processor(feed, new Store()).ProcessNextBatchAsync(CancellationToken.None);

        var span = Assert.Single(captured);
        Assert.Equal(ActivityKind.Consumer, span.Kind);
        var link = Assert.Single(span.Links);
        Assert.Equal(origin.TraceId, link.Context.TraceId);
        Assert.Equal(origin.SpanId, link.Context.SpanId);
    }

    private static ProjectionProcessor Processor(Feed feed, Store store) =>
        new(feed, store, new ProjectionOptions(100, TimeSpan.Zero, true));

    private sealed class Feed : IEventFeedClient
    {
        private readonly List<FeedEvent> _events = [];

        public FeedEvent Registered(Guid tx, string? scope = "Personal", decimal amount = 10m, string? traceParent = null) =>
            Add(FeedEventTypes.TransactionRegistered, 1, tx, new
            {
                transactionId = tx,
                ownerUserId = Guid.Empty,
                accountId = Guid.Empty,
                accountScope = scope,
                transactionType = "Expense",
                paymentMethod = "Pix",
                category = "Mercado",
                amount,
                currency = "BRL",
                occurredOn = "2026-11-19",
            }, traceParent);

        public FeedEvent Deleted(Guid tx) =>
            Add(FeedEventTypes.TransactionDeleted, 1, tx, new { transactionId = tx, ownerUserId = Guid.Empty });

        public FeedEvent Raw(string type, int version, object payload) => Add(type, version, Guid.NewGuid(), payload);

        /// <summary>Entrega de novo o mesmo evento (mesmo eventId), numa sequencia nova.</summary>
        public void Redeliver(FeedEvent original) => _events.Add(original with { Sequence = _events.Count + 1 });

        /// <summary>Head informado pelo feed; 0 simula um transactions anterior ao campo.</summary>
        public long? HeadSequence { get; set; }

        public Task<FeedPage> ReadAsync(long afterSequence, int limit, CancellationToken cancellationToken)
        {
            var page = _events.Where(e => e.Sequence > afterSequence).Take(limit).ToArray();
            return Task.FromResult(new FeedPage(page, page.Length > 0 ? page[^1].Sequence : afterSequence, HeadSequence ?? _events.Count));
        }

        private FeedEvent Add(string type, int version, Guid aggregate, object payload, string? traceParent = null)
        {
            var feedEvent = new FeedEvent(
                _events.Count + 1, Guid.NewGuid(), type, version, aggregate, Guid.Empty, DateTimeOffset.UtcNow,
                traceParent, JsonSerializer.SerializeToElement(payload, Json));
            _events.Add(feedEvent);
            return feedEvent;
        }
    }

    private sealed class Store : IProjectionStore
    {
        private readonly Dictionary<Guid, ProjectedTransaction> _pending = [];
        private readonly HashSet<Guid> _pendingRemovals = [];
        private long _pendingCheckpoint = -1;

        public Dictionary<Guid, ProjectedTransaction> Rows { get; } = [];

        public long Checkpoint { get; set; }

        public int Saves { get; private set; }

        public long? CheckpointAtSave { get; private set; }

        public Task<long> GetCheckpointAsync(CancellationToken cancellationToken) => Task.FromResult(Checkpoint);

        public Task<ProjectedTransaction?> FindAsync(Guid transactionId, CancellationToken cancellationToken) =>
            Task.FromResult(
                _pendingRemovals.Contains(transactionId) ? null
                : _pending.GetValueOrDefault(transactionId) ?? Rows.GetValueOrDefault(transactionId));

        public void Add(ProjectedTransaction transaction)
        {
            _pendingRemovals.Remove(transaction.TransactionId);
            _pending[transaction.TransactionId] = transaction;
        }

        public void Remove(ProjectedTransaction transaction)
        {
            _pending.Remove(transaction.TransactionId);
            _pendingRemovals.Add(transaction.TransactionId);
        }

        public void SetCheckpoint(long sequence) => _pendingCheckpoint = sequence;

        // Nada fica visivel antes do commit: prova que projecao e checkpoint entram juntos.
        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            foreach (var id in _pendingRemovals)
            {
                Rows.Remove(id);
            }

            foreach (var (id, row) in _pending)
            {
                Rows[id] = row;
            }

            if (_pendingCheckpoint >= 0)
            {
                Checkpoint = _pendingCheckpoint;
            }

            CheckpointAtSave = Checkpoint;
            Saves++;
            _pending.Clear();
            _pendingRemovals.Clear();
            _pendingCheckpoint = -1;
            return Task.CompletedTask;
        }
    }
}
