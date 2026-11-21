using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Application.Transactions;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.UnitTests;

/// <summary>EVT-1: os casos de uso gravam os eventos certos no outbox, no mesmo commit da mudanca.</summary>
public sealed class EventHandlersTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_AddsTransactionRegistered_WithTheAccountScope_BeforeSaving()
    {
        var ports = new Ports(accountScope: "Business");
        var accountId = Guid.NewGuid();

        var result = await ports.CreateHandler().HandleAsync(Command(accountId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var registered = Assert.IsType<TransactionRegistered>(Assert.Single(ports.Outbox.Events));
        Assert.Equal(result.Value.Id, registered.TransactionId);
        Assert.Equal(accountId, registered.AccountId);
        Assert.Equal("Business", registered.AccountScope);
        Assert.Equal("Expense", registered.TransactionType);
        Assert.Equal("Pix", registered.PaymentMethod);
        Assert.Equal(99.9m, registered.Amount);
        Assert.Equal(1, ports.UnitOfWork.Saves);
        Assert.Equal(1, ports.Outbox.EventsAtFirstSave);
    }

    [Fact]
    public async Task Create_ForAnAccountThatDoesNotExist_AddsNoEventAndDoesNotSave()
    {
        var ports = new Ports(accountScope: null);

        var result = await ports.CreateHandler().HandleAsync(Command(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("account.not_found", result.Error.Code);
        Assert.Empty(ports.Outbox.Events);
        Assert.Equal(0, ports.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Delete_AddsTransactionDeleted_BeforeSaving()
    {
        var ports = new Ports(accountScope: "Personal");
        var created = await ports.CreateHandler().HandleAsync(Command(Guid.NewGuid()), CancellationToken.None);
        ports.Outbox.Events.Clear();
        ports.UnitOfWork.Reset();

        var result = await new DeleteTransactionHandler(ports.Repository, ports.Outbox, ports.UnitOfWork)
            .HandleAsync(created.Value.Id, Owner, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var deleted = Assert.IsType<TransactionDeleted>(Assert.Single(ports.Outbox.Events));
        Assert.Equal(created.Value.Id, deleted.TransactionId);
        Assert.Equal(Owner, deleted.OwnerUserId);
        Assert.Equal(1, ports.Outbox.EventsAtFirstSave);
    }

    [Fact]
    public async Task Delete_OfAnotherOwnersTransaction_AddsNoEvent()
    {
        var ports = new Ports(accountScope: "Personal");
        var created = await ports.CreateHandler().HandleAsync(Command(Guid.NewGuid()), CancellationToken.None);
        ports.Outbox.Events.Clear();

        var result = await new DeleteTransactionHandler(ports.Repository, ports.Outbox, ports.UnitOfWork)
            .HandleAsync(created.Value.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(ports.Outbox.Events);
    }

    [Fact]
    public async Task Feed_ServesOnlyTheContiguousPrefixOlderThanTheMargin()
    {
        // Sequencia 2 ainda esta dentro da margem; a 3 e antiga, mas vem depois da 2:
        // servir a 3 faria o leitor pular a 2 de vez.
        var margin = TimeSpan.FromSeconds(5);
        var feed = new StubFeed(
            Event(1, Now - TimeSpan.FromSeconds(30)),
            Event(2, Now - TimeSpan.FromSeconds(1)),
            Event(3, Now - TimeSpan.FromSeconds(20)));

        var page = await new ReadEventFeedHandler(feed, new EventFeedOptions(margin), new FixedTime(Now))
            .HandleAsync(0, 10, CancellationToken.None);

        Assert.Equal([1L], page.Events.Select(e => e.Sequence));
        Assert.Equal(1, page.LastSequence);
        Assert.Equal(3, page.HeadSequence);
    }

    [Fact]
    public async Task Feed_KeepsTheCursor_WhenNothingIsVisible()
    {
        var feed = new StubFeed(Event(8, Now));

        var page = await new ReadEventFeedHandler(feed, new EventFeedOptions(TimeSpan.FromSeconds(5)), new FixedTime(Now))
            .HandleAsync(7, 10, CancellationToken.None);

        Assert.Empty(page.Events);
        Assert.Equal(7, page.LastSequence);
        Assert.Equal(8, page.HeadSequence);
    }

    private static readonly Guid Owner = Guid.NewGuid();

    private static CreateTransactionCommand Command(Guid accountId) => new(
        Owner, accountId, TransactionType.Expense, PaymentMethod.Pix, null,
        "Almoco", "Alimentacao", 99.9m, "BRL", new DateOnly(2026, 11, 19), null);

    private static FeedEvent Event(long sequence, DateTimeOffset occurredAt) => new(
        sequence, Guid.NewGuid(), TransactionRegistered.Name, 1, Guid.NewGuid(), Guid.NewGuid(), occurredAt, null, "{}");

    private sealed class Ports(string? accountScope)
    {
        public InMemoryRepository Repository { get; } = new();

        public RecordingOutbox Outbox { get; } = new();

        public CountingUnitOfWork UnitOfWork { get; private set; } = null!;

        public CreateTransactionHandler CreateHandler()
        {
            UnitOfWork = new CountingUnitOfWork(Outbox);
            return new CreateTransactionHandler(Repository, new StubAccounts(accountScope), Outbox, UnitOfWork, new FixedTime(Now));
        }
    }

    private sealed class InMemoryRepository : ITransactionRepository
    {
        private readonly List<Transaction> _items = [];

        public Task<Transaction?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult(_items.FirstOrDefault(t => t.Id == id && t.OwnerUserId == ownerUserId));

        public Task<IReadOnlyList<Transaction>> ListAsync(TransactionQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Transaction>>(_items.Where(t => t.OwnerUserId == query.OwnerUserId).ToList());

        public Task AddAsync(Transaction transaction, CancellationToken cancellationToken)
        {
            _items.Add(transaction);
            return Task.CompletedTask;
        }

        public void Remove(Transaction transaction) => _items.Remove(transaction);

        public Task<IReadOnlyList<Transaction>> ListByImportBatchAsync(
            Guid ownerUserId, Guid importBatchId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Transaction>>(_items.Where(t => t.OwnerUserId == ownerUserId && t.ImportBatchId == importBatchId).ToList());

        public Task<IReadOnlySet<string>> FindExistingExternalIdsAsync(
            Guid ownerUserId, Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }

    private sealed class RecordingOutbox : IEventOutbox
    {
        public List<TransactionEvent> Events { get; } = [];

        /// <summary>Quantos eventos ja estavam no outbox no primeiro SaveChanges (prova o mesmo commit).</summary>
        public int? EventsAtFirstSave { get; set; }

        public void Add(TransactionEvent @event) => Events.Add(@event);
    }

    private sealed class CountingUnitOfWork(RecordingOutbox outbox) : IUnitOfWork
    {
        public int Saves { get; private set; }

        public void Reset()
        {
            Saves = 0;
            outbox.EventsAtFirstSave = null;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;
            outbox.EventsAtFirstSave ??= outbox.Events.Count;
            return Task.FromResult(1);
        }
    }

    private sealed class StubAccounts(string? scope) : IAccountsClient
    {
        public Task<string?> FindAccountScopeAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(scope);

        public Task<bool> CreditCardExistsAsync(Guid creditCardId, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class StubFeed(params FeedEvent[] events) : IEventFeed
    {
        public Task<IReadOnlyList<FeedEvent>> ReadAfterAsync(long afterSequence, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FeedEvent>>(events.Where(e => e.Sequence > afterSequence).Take(limit).ToArray());

        public Task<long> GetHeadSequenceAsync(CancellationToken cancellationToken) =>
            Task.FromResult(events.Length == 0 ? 0 : events.Max(e => e.Sequence));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
