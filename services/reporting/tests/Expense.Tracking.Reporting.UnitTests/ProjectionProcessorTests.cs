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
    public async Task ScopeResolved_CompletesTheScopeOfALegacyRow()
    {
        var feed = new Feed();
        var store = new Store();
        var tx = Guid.NewGuid();
        feed.Registered(tx, scope: null, amount: 80m);
        var resolved = ScopeResolved(feed, tx, "Business");

        var result = await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        Assert.Equal((2, 0), (result.Applied, result.Skipped));
        var row = Assert.Single(store.Rows.Values);
        Assert.Equal("Business", row.AccountScope);
        Assert.Equal(80m, row.Amount);
        Assert.Equal(resolved.Sequence, row.LastEventSequence);
    }

    [Fact]
    public async Task ScopeResolved_ForADeletedTransaction_DoesNotRecreateIt()
    {
        var feed = new Feed();
        var store = new Store();
        var tx = Guid.NewGuid();
        feed.Registered(tx, scope: null);
        feed.Deleted(tx);
        ScopeResolved(feed, tx, "Personal");

        var result = await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        Assert.Equal(3, result.Applied);
        Assert.Empty(store.Rows);
    }

    private static FeedEvent ScopeResolved(Feed feed, Guid tx, string scope) =>
        feed.Raw(FeedEventTypes.TransactionAccountScopeResolved, 1, new
        {
            transactionId = tx,
            ownerUserId = Guid.Empty,
            accountId = Guid.Empty,
            accountScope = scope,
        });

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
        feed.Raw(FeedEventTypes.TransactionRegistered, version: 3, new { transactionId = Guid.NewGuid() });

        await Assert.ThrowsAsync<UnsupportedEventVersionException>(
            () => Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None));

        Assert.Equal(0, store.Saves);
        Assert.Equal(0, store.Checkpoint);
    }

    // ---------- INV-2: TransactionRegistered v2 e TransactionCardDetailsResolved ----------

    [Fact]
    public async Task RegisteredV2_StoresDescriptionCardAndInstallments()
    {
        var feed = new Feed();
        var store = new Store();
        var tx = Guid.NewGuid();
        var card = Guid.NewGuid();
        feed.RegisteredV2(tx, card, installments: 3, description: "Notebook");

        await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        var row = Assert.Single(store.Rows.Values);
        Assert.Equal(("Notebook", card, 3, "CreditCard"), (row.Description, row.CreditCardId, row.Installments, row.PaymentMethod));
    }

    [Fact]
    public async Task RegisteredV1_IsReadAsCashWithoutCard_AndV1AndV2LiveSideBySide()
    {
        var feed = new Feed();
        var store = new Store();
        var (old, current) = (Guid.NewGuid(), Guid.NewGuid());
        feed.Registered(old);
        feed.RegisteredV2(current, Guid.NewGuid(), installments: 2, description: "Atual");

        var result = await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        Assert.Equal(2, result.Applied);
        Assert.Equal((null, null, 1), (store.Rows[old].Description, store.Rows[old].CreditCardId, store.Rows[old].Installments));
        Assert.Equal(2, store.Rows[current].Installments);
    }

    [Fact]
    public async Task CardDetailsResolved_CompletesAV1Row_AndIsIgnoredForAMissingOne()
    {
        var feed = new Feed();
        var store = new Store();
        var (tx, deleted, card) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        feed.Registered(tx);
        CardDetails(feed, tx, card, "Compra antiga");
        CardDetails(feed, deleted, card, "Ja excluida");

        var result = await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        Assert.Equal(3, result.Applied);
        var row = Assert.Single(store.Rows.Values);
        Assert.Equal(("Compra antiga", card, 1, 2L), (row.Description, row.CreditCardId, row.Installments, row.LastEventSequence));
    }

    [Fact]
    public async Task RebuildFromZero_WithV1RegisteredThenCardDetails_ReachesTheSameState()
    {
        var feed = new Feed();
        var store = new Store();
        var (tx, card) = (Guid.NewGuid(), Guid.NewGuid());
        feed.Registered(tx);
        CardDetails(feed, tx, card, "Compra antiga");
        await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        store.Checkpoint = 0;
        await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        Assert.Equal(card, Assert.Single(store.Rows.Values).CreditCardId);
    }

    [Fact]
    public async Task CardInvoicePaid_IsAnUpsertOutsideTheTransactions_AndDeletionRemovesIt()
    {
        var feed = new Feed();
        var store = new Store();
        var (payment, card) = (Guid.NewGuid(), Guid.NewGuid());
        var paid = InvoicePaid(feed, payment, card, "2026-12", 1500m);
        feed.Redeliver(paid);

        await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);
        store.Checkpoint = 0; // reconstrucao: reaplicar nao duplica
        await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        var row = Assert.Single(store.Payments.Values);
        Assert.Equal((card, new DateOnly(2026, 12, 1), 1500m, new DateOnly(2026, 12, 15)),
            (row.CreditCardId, row.InvoiceDueMonth, row.Amount, row.PaidOn));
        Assert.Empty(store.Rows); // nunca vira lancamento

        feed.Raw(FeedEventTypes.CardInvoicePaymentDeleted, 1, new { paymentId = payment, ownerUserId = Guid.Empty, creditCardId = card });
        feed.Raw(FeedEventTypes.CardInvoicePaymentDeleted, 1, new { paymentId = Guid.NewGuid(), ownerUserId = Guid.Empty, creditCardId = card });
        var result = await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        Assert.Equal(2, result.Applied);
        Assert.Empty(store.Payments);
    }

    [Fact]
    public async Task CardInvoicePaid_InAnUnknownVersion_StopsTheBatch()
    {
        var feed = new Feed();
        var store = new Store();
        feed.Raw(FeedEventTypes.CardInvoicePaid, 2, new { paymentId = Guid.NewGuid() });

        await Assert.ThrowsAsync<UnsupportedEventVersionException>(
            () => Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None));
        Assert.Equal(0, store.Saves);
    }

    private static FeedEvent InvoicePaid(Feed feed, Guid payment, Guid card, string dueMonth, decimal amount) =>
        feed.Raw(FeedEventTypes.CardInvoicePaid, 1, new
        {
            paymentId = payment,
            ownerUserId = Guid.Empty,
            creditCardId = card,
            invoiceDueMonth = dueMonth,
            accountId = Guid.NewGuid(),
            amount,
            currency = "BRL",
            paidOn = "2026-12-15",
        });

    // ---------- REC-4 (ADR-0008): regras e periodos fechados ----------

    private static FeedEvent RecurrenceSaved(Feed feed, Guid rule, decimal amount, int version = 1, object[]? amounts = null) =>
        feed.Raw(FeedEventTypes.RecurrenceSaved, version, new
        {
            amounts,
            recurrenceId = rule,
            ownerUserId = Guid.Empty,
            accountId = Guid.Empty,
            accountScope = "Business",
            transactionType = "Expense",
            paymentMethod = "Boleto",
            creditCardId = (Guid?)null,
            description = "Aluguel",
            category = "Moradia",
            amount,
            currency = "BRL",
            frequency = "Monthly",
            startsOn = "2026-12-10",
            endsOn = (string?)null,
            recurrenceVersion = 1,
        });

    private static FeedEvent Closed(Feed feed, Guid rule, string period, Guid? tx = null) =>
        feed.Raw(FeedEventTypes.RecurrenceOccurrenceClosed, 1, new
        {
            recurrenceId = rule,
            ownerUserId = Guid.Empty,
            period,
            status = tx is null ? "Skipped" : "Launched",
            transactionId = tx,
        });

    [Fact]
    public async Task Recurrences_AreUpserted_PeriodsCloseAndReopen_AndReplayingChangesNothing()
    {
        var feed = new Feed();
        var store = new Store();
        var (rule, tx) = (Guid.NewGuid(), Guid.NewGuid());
        RecurrenceSaved(feed, rule, 2000m);
        RecurrenceSaved(feed, rule, 2150m);
        Closed(feed, rule, "2026-12", tx);
        Closed(feed, rule, "2027-01");
        feed.Raw(FeedEventTypes.RecurrenceOccurrenceReopened, 1, new { recurrenceId = rule, ownerUserId = Guid.Empty, period = "2027-01" });

        var result = await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);
        store.Checkpoint = 0;
        await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        Assert.Equal(5, result.Applied);
        var row = Assert.Single(store.Recurrences.Values);
        Assert.Equal((2150m, "Business", "Monthly", new DateOnly(2026, 12, 10)), (row.Amount, row.AccountScope, row.Frequency, row.StartsOn));
        var closed = Assert.Single(store.Occurrences.Values);
        Assert.Equal(("2026-12", "Launched", tx), (closed.Period, closed.Status, closed.TransactionId));
    }

    [Fact]
    public async Task RecurrenceSaved_V1BecomesOneAmount_AndV2ReplacesTheAmounts()
    {
        // RJT-3 (ADR-0010, decisao 7).
        var feed = new Feed();
        var store = new Store();
        var rule = Guid.NewGuid();
        RecurrenceSaved(feed, rule, 2000m);
        await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);
        Assert.Equal([("2026-12", 2000m)], store.Recurrences[rule].Amounts.Select(a => (a.FromPeriod, a.Amount)));

        RecurrenceSaved(feed, rule, 2100m, version: 2, amounts:
        [
            new { fromPeriod = "2027-03", amount = 2100m },
            new { fromPeriod = "2026-12", amount = 2000m },
        ]);
        await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        var row = store.Recurrences[rule];
        Assert.Equal(2000m, row.Amount); // a primeira vigencia
        Assert.Equal([("2026-12", 2000m), ("2027-03", 2100m)], row.Amounts.Select(a => (a.FromPeriod, a.Amount)));
    }

    [Fact]
    public async Task DeletingARecurrence_RemovesItAndItsClosedPeriods_AndANewerVersionStopsTheBatch()
    {
        var feed = new Feed();
        var store = new Store();
        var rule = Guid.NewGuid();
        RecurrenceSaved(feed, rule, 2000m);
        Closed(feed, rule, "2026-12");
        feed.Raw(FeedEventTypes.RecurrenceDeleted, 1, new { recurrenceId = rule, ownerUserId = Guid.Empty });

        await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        Assert.Empty(store.Recurrences);
        Assert.Empty(store.Occurrences);

        RecurrenceSaved(feed, Guid.NewGuid(), 10m, version: 3);
        await Assert.ThrowsAsync<UnsupportedEventVersionException>(() => Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None));
    }

    // ---------- LNC-4 (ADR-0007): TransactionUpdated ----------

    private static FeedEvent Updated(Feed feed, Guid tx, decimal amount, string category = "Feira", Guid? card = null, int installments = 1,
        string type = "Expense", string occurredOn = "2026-11-20") =>
        feed.Raw(FeedEventTypes.TransactionUpdated, 1, new
        {
            transactionId = tx,
            ownerUserId = Guid.Empty,
            accountId = Guid.Empty,
            transactionType = type,
            paymentMethod = card is null ? "Pix" : "CreditCard",
            category,
            amount,
            currency = "BRL",
            occurredOn,
            description = "Corrigido",
            creditCardId = card,
            installments,
            transactionVersion = 2,
        });

    [Fact]
    public async Task Updated_CorrectsTheRow_AndKeepsTheScope()
    {
        var feed = new Feed();
        var store = new Store();
        var (tx, card) = (Guid.NewGuid(), Guid.NewGuid());
        feed.Registered(tx, scope: "Business", amount: 100m);
        Updated(feed, tx, 3000m, category: "Eletronicos", card: card, installments: 6, type: "Income", occurredOn: "2026-10-31");

        var result = await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        Assert.Equal((2, 0), (result.Applied, result.Skipped));
        var row = Assert.Single(store.Rows.Values);
        Assert.Equal(("Business", "Income", "CreditCard", "Eletronicos", 3000m, new DateOnly(2026, 10, 31), "Corrigido", card, 6, 2L),
            (row.AccountScope, row.TransactionType, row.PaymentMethod, row.Category, row.Amount, row.OccurredOn, row.Description,
                row.CreditCardId, row.Installments, row.LastEventSequence));
    }

    [Fact]
    public async Task Updated_AfterTheDeletion_DoesNotBringTheTransactionBack()
    {
        var feed = new Feed();
        var store = new Store();
        var tx = Guid.NewGuid();
        feed.Registered(tx);
        feed.Deleted(tx);
        Updated(feed, tx, 50m);

        var result = await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        Assert.Equal(3, result.Applied);
        Assert.Empty(store.Rows);
    }

    [Fact]
    public async Task RebuildFromZero_WithEdits_ReachesTheLastState()
    {
        var feed = new Feed();
        var store = new Store();
        var tx = Guid.NewGuid();
        feed.Registered(tx, amount: 100m);
        Updated(feed, tx, 150m);
        Updated(feed, tx, 175m, category: "Padaria");
        await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        store.Checkpoint = 0;
        await Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None);

        var row = Assert.Single(store.Rows.Values);
        Assert.Equal((175m, "Padaria"), (row.Amount, row.Category));
    }

    [Fact]
    public async Task Updated_InAnUnknownVersion_StopsTheBatch()
    {
        var feed = new Feed();
        var store = new Store();
        feed.Raw(FeedEventTypes.TransactionUpdated, 2, new { transactionId = Guid.NewGuid() });

        await Assert.ThrowsAsync<UnsupportedEventVersionException>(
            () => Processor(feed, store).ProcessNextBatchAsync(CancellationToken.None));
        Assert.Equal(0, store.Saves);
    }

    private static FeedEvent CardDetails(Feed feed, Guid tx, Guid card, string description) =>
        feed.Raw(FeedEventTypes.TransactionCardDetailsResolved, 1, new
        {
            transactionId = tx,
            ownerUserId = Guid.Empty,
            description,
            creditCardId = card,
            installments = 1,
        });

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

        public FeedEvent RegisteredV2(Guid tx, Guid? card, int installments, string description) =>
            Add(FeedEventTypes.TransactionRegistered, 2, tx, new
            {
                transactionId = tx,
                ownerUserId = Guid.Empty,
                accountId = Guid.Empty,
                accountScope = "Personal",
                transactionType = "Expense",
                paymentMethod = card is null ? "Pix" : "CreditCard",
                category = "Eletronicos",
                amount = 300m,
                currency = "BRL",
                occurredOn = "2026-11-19",
                description,
                creditCardId = card,
                installments,
            });

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

        public Dictionary<Guid, ProjectedCardPayment> Payments { get; } = [];

        public Task<ProjectedCardPayment?> FindPaymentAsync(Guid paymentId, CancellationToken cancellationToken) =>
            Task.FromResult(Payments.GetValueOrDefault(paymentId));

        public void AddPayment(ProjectedCardPayment payment) => Payments[payment.PaymentId] = payment;

        public void RemovePayment(ProjectedCardPayment payment) => Payments.Remove(payment.PaymentId);

        // REC-4: regras e periodos fechados (aplicados direto; os testes de recorrencia olham o resultado).
        public Dictionary<Guid, ProjectedRecurrence> Recurrences { get; } = [];

        public Dictionary<(Guid, string), ProjectedRecurrenceOccurrence> Occurrences { get; } = [];

        public Task<ProjectedRecurrence?> FindRecurrenceAsync(Guid recurrenceId, CancellationToken cancellationToken) =>
            Task.FromResult(Recurrences.GetValueOrDefault(recurrenceId));

        public void AddRecurrence(ProjectedRecurrence recurrence) => Recurrences[recurrence.RecurrenceId] = recurrence;

        public Task RemoveRecurrenceAsync(ProjectedRecurrence recurrence, CancellationToken cancellationToken)
        {
            Recurrences.Remove(recurrence.RecurrenceId);
            foreach (var key in Occurrences.Keys.Where(k => k.Item1 == recurrence.RecurrenceId).ToList())
            {
                Occurrences.Remove(key);
            }

            return Task.CompletedTask;
        }

        public Task<ProjectedRecurrenceOccurrence?> FindRecurrenceOccurrenceAsync(Guid recurrenceId, string period, CancellationToken cancellationToken) =>
            Task.FromResult(Occurrences.GetValueOrDefault((recurrenceId, period)));

        public void AddRecurrenceOccurrence(ProjectedRecurrenceOccurrence occurrence) =>
            Occurrences[(occurrence.RecurrenceId, occurrence.Period)] = occurrence;

        public void RemoveRecurrenceOccurrence(ProjectedRecurrenceOccurrence occurrence) =>
            Occurrences.Remove((occurrence.RecurrenceId, occurrence.Period));

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
