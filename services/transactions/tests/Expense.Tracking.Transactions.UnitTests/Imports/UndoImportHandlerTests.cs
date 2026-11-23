using System.Diagnostics.Metrics;
using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Application.Imports;
using Expense.Tracking.Transactions.Domain.Imports;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.UnitTests.Imports;

/// <summary>
/// IMP-9: desfazer que perde a corrida (o mesmo lote desfeito em paralelo, ou um lancamento do lote
/// excluido a mao no meio) recarrega e decide de novo, sem evento repetido nem erro.
/// </summary>
public sealed class UndoImportHandlerTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-21T12:00:00Z");

    /// <summary>Lote confirmado com dois lancamentos; o mesmo id em todas as copias (recarregar).</summary>
    private static (ImportBatch Batch, IReadOnlyList<Transaction> Created) ConfirmedBatch(Guid id)
    {
        var batch = ImportBatch.Create(
            Owner, Account, null, PaymentMethod.Pix, ImportFormat.Ofx,
            new ParsedStatement(StatementKind.BankAccount, "BRL",
            [
                StatementLine.Valid(1, new DateOnly(2026, 10, 1), -10m, "A", "A"),
                StatementLine.Valid(2, new DateOnly(2026, 10, 1), -20m, "B", "B"),
            ]),
            new HashSet<string>(), Now).Value;
        typeof(ImportBatch).GetProperty(nameof(ImportBatch.Id))!.SetValue(batch, id);
        var created = batch.Confirm(
            batch.Lines.Select(l => new ImportLineDecision(l.Id, null, "Mercado", null)).ToList(),
            new HashSet<string>(), Now).Value.Transactions;
        return (batch, created);
    }

    [Fact]
    public async Task Undo_RemovesEachTransactionWithOneDeletedEvent()
    {
        var id = Guid.NewGuid();
        var (batch, created) = ConfirmedBatch(id);
        var transactions = new FakeTransactions { Existing = created.ToList() };
        var outbox = new RecordingOutbox();

        var result = await Handler(new SequenceBatches(batch), transactions, outbox, new ConflictingUnitOfWork(conflicts: 0))
            .HandleAsync(new UndoImportCommand(Owner, id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Undone", result.Value.Status);
        Assert.Equal(created.Select(t => t.Id), transactions.Removed.Select(t => t.Id));
        Assert.Equal(created.Select(t => t.Id), outbox.Events.OfType<TransactionDeleted>().Select(e => e.TransactionId));
    }

    [Fact]
    public async Task LosingTheRaceToAnotherUndo_ReturnsTheUndoneBatchWithoutDeletingAgain()
    {
        var id = Guid.NewGuid();
        var (mine, created) = ConfirmedBatch(id);
        var (winner, _) = ConfirmedBatch(id);
        winner.Undo(Now);

        var outbox = new RecordingOutbox();
        var unitOfWork = new ConflictingUnitOfWork(conflicts: 1) { OnReset = outbox.Events.Clear };

        var result = await Handler(new SequenceBatches(mine, winner), new FakeTransactions { Existing = created.ToList() }, outbox, unitOfWork)
            .HandleAsync(new UndoImportCommand(Owner, id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Undone", result.Value.Status);
        Assert.Equal(1, unitOfWork.Saves); // so a tentativa que perdeu chegou a gravar
        Assert.Empty(outbox.Events);
    }

    [Fact]
    public async Task TransactionDeletedByHandInTheMiddle_RetriesWithoutIt()
    {
        var id = Guid.NewGuid();
        var (first, created) = ConfirmedBatch(id);
        var (reloaded, _) = ConfirmedBatch(id);

        var transactions = new FakeTransactions { Existing = created.ToList() };
        var outbox = new RecordingOutbox();
        // Entre a leitura e a gravacao, o usuario excluiu o primeiro lancamento a mao.
        var unitOfWork = new ConflictingUnitOfWork(conflicts: 1)
        {
            OnReset = () =>
            {
                outbox.Events.Clear();
                transactions.Existing.RemoveAt(0);
            },
        };

        var result = await Handler(new SequenceBatches(first, reloaded), transactions, outbox, unitOfWork)
            .HandleAsync(new UndoImportCommand(Owner, id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, unitOfWork.Saves);
        Assert.Equal([created[1].Id], outbox.Events.OfType<TransactionDeleted>().Select(e => e.TransactionId));
    }

    private static UndoImportHandler Handler(
        IImportBatchRepository batches, FakeTransactions transactions, RecordingOutbox outbox, IUnitOfWork unitOfWork) =>
        new(batches, transactions, new CardPaymentTests.InMemoryPayments(), new NoRecurrences(), outbox, unitOfWork, new ImportMetrics(new TestMeterFactory()), new FixedTime());

    private sealed class SequenceBatches(params ImportBatch[] sequence) : IImportBatchRepository
    {
        private int _calls;

        public Task<ImportBatch?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult<ImportBatch?>(sequence[Math.Min(_calls++, sequence.Length - 1)]);

        public Task AddAsync(ImportBatch batch, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> DeleteExpiredAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    /// <summary>As primeiras <paramref name="conflicts"/> gravacoes perdem a corrida.</summary>
    private sealed class ConflictingUnitOfWork(int conflicts) : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Action? OnReset { get; init; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;
            return Saves <= conflicts
                ? throw new PersistenceConflictException("corrida", new InvalidOperationException())
                : Task.FromResult(1);
        }

        public void Reset() => OnReset?.Invoke();
    }

    private sealed class FakeTransactions : ITransactionRepository
    {
        public List<Transaction> Existing { get; init; } = [];

        public List<Transaction> Removed { get; } = [];

        public Task<IReadOnlyDictionary<string, string>> FindLatestCategoriesAsync(
            Guid ownerUserId, IReadOnlyCollection<string> descriptionKeys, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public Task<IReadOnlyList<Transaction>> ListByImportBatchAsync(
            Guid ownerUserId, Guid importBatchId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Transaction>>(Existing.Where(t => t.ImportBatchId == importBatchId).ToList());

        public void Remove(Transaction transaction) => Removed.Add(transaction);

        public Task<IReadOnlyList<string>> ListCategoriesAsync(
            Guid ownerUserId, IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlySet<string>> FindExistingExternalIdsAsync(
            Guid ownerUserId, Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task AddAsync(Transaction transaction, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Transaction?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult<Transaction?>(null);

        public Task<IReadOnlyList<Transaction>> ListAsync(TransactionQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Transaction>>([]);

        public Task<IReadOnlyList<Transaction>> ListByIdsAsync(
            Guid ownerUserId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Transaction>>([]);
    }

    private sealed class RecordingOutbox : IEventOutbox
    {
        public List<TransactionEvent> Events { get; } = [];

        public void Add(TransactionEvent @event) => Events.Add(@event);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);

        public void Dispose()
        {
        }
    }
}
