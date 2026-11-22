using System.Diagnostics.Metrics;
using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Application.Imports;
using Expense.Tracking.Transactions.Domain.Imports;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.UnitTests.Imports;

/// <summary>
/// IMP-5: a confirmacao que perde a corrida (outra confirmacao do mesmo lote, ou a mesma chave
/// gravada por outro lote) recarrega e decide de novo, sem criar lancamento repetido.
/// </summary>
public sealed class ConfirmImportHandlerTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-21T12:00:00Z");

    private static ImportBatch PendingBatch() => ImportBatch.Create(
        Owner, Guid.NewGuid(), null, PaymentMethod.Pix, ImportFormat.Ofx,
        new ParsedStatement(StatementKind.BankAccount, "BRL",
        [
            StatementLine.Valid(1, new DateOnly(2026, 10, 1), -10m, "A", "A"),
            StatementLine.Valid(2, new DateOnly(2026, 10, 1), -20m, "B", "B"),
        ]),
        new HashSet<string>(), Now).Value;

    private static List<ImportLineDecision> WithCategory(ImportBatch batch) =>
        batch.Lines.Select(l => new ImportLineDecision(l.Id, null, "Mercado", null)).ToList();

    [Fact]
    public async Task LosingTheRaceToAnotherConfirmation_ReturnsTheConfirmedBatchWithoutCreatingAgain()
    {
        var mine = PendingBatch();
        var winner = PendingBatch();
        // O "vencedor" e o mesmo lote ja confirmado por outra request.
        typeof(ImportBatch).GetProperty(nameof(ImportBatch.Id))!.SetValue(winner, mine.Id);
        winner.Confirm(WithCategory(winner), new HashSet<string>(), Now);

        var batches = new SequenceBatches(mine, winner);
        var unitOfWork = new ConflictOnceUnitOfWork();
        var transactions = new RecordingTransactions();
        var outbox = new RecordingOutbox();

        var result = await Handler(batches, transactions, outbox, unitOfWork)
            .HandleAsync(new ConfirmImportCommand(Owner, mine.Id, WithCategory(mine)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Confirmed", result.Value.Status);
        Assert.Equal(1, unitOfWork.Resets);
        Assert.Equal(1, unitOfWork.Saves); // so a tentativa que perdeu chegou a gravar
        Assert.Equal(2, result.Value.Summary.Imported);
    }

    [Fact]
    public async Task LosingTheRaceToAnotherBatch_RetriesAndTheLineComesBackDuplicate()
    {
        var first = PendingBatch();
        var reloaded = PendingBatch();
        // Recarregar traz o mesmo lote, com as mesmas linhas.
        typeof(ImportBatch).GetProperty(nameof(ImportBatch.Id))!.SetValue(reloaded, first.Id);
        foreach (var (line, original) in reloaded.Lines.Zip(first.Lines))
        {
            typeof(ImportLine).GetProperty(nameof(ImportLine.Id))!.SetValue(line, original.Id);
        }

        var transactions = new RecordingTransactions();
        // Na segunda tentativa, a chave "B" ja foi lancada por outro lote.
        transactions.LaunchedOnRetry.Add("B");
        var unitOfWork = new ConflictOnceUnitOfWork { OnReset = () => transactions.Retrying = true };

        var result = await Handler(new SequenceBatches(first, reloaded), transactions, new RecordingOutbox(), unitOfWork)
            .HandleAsync(new ConfirmImportCommand(Owner, first.Id, WithCategory(first)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Summary.Imported);
        Assert.Equal(1, result.Value.Summary.Duplicate);
        Assert.Equal(2, unitOfWork.Saves);
    }

    private static ConfirmImportHandler Handler(
        IImportBatchRepository batches, RecordingTransactions transactions, RecordingOutbox outbox, IUnitOfWork unitOfWork) =>
        new(batches, transactions, new CardPaymentTests.InMemoryPayments(), new AllAccountsExist(), outbox, unitOfWork,
            new ImportMetrics(new TestMeterFactory()), new FixedTime());

    private sealed class SequenceBatches(params ImportBatch[] sequence) : IImportBatchRepository
    {
        private int _calls;

        public Task<ImportBatch?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult<ImportBatch?>(sequence[Math.Min(_calls++, sequence.Length - 1)]);

        public Task AddAsync(ImportBatch batch, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> DeleteExpiredAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class ConflictOnceUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public int Resets { get; private set; }

        public Action? OnReset { get; init; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;
            return Saves == 1
                ? throw new PersistenceConflictException("corrida", new InvalidOperationException())
                : Task.FromResult(1);
        }

        public void Reset()
        {
            Resets++;
            OnReset?.Invoke();
        }
    }

    private sealed class RecordingTransactions : ITransactionRepository
    {
        public HashSet<string> LaunchedOnRetry { get; } = [];

        public bool Retrying { get; set; }

        public Task<IReadOnlySet<string>> FindExistingExternalIdsAsync(
            Guid ownerUserId, Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(Retrying ? LaunchedOnRetry : new HashSet<string>());

        public Task AddAsync(Transaction transaction, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Transaction?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult<Transaction?>(null);

        public Task<IReadOnlyList<Transaction>> ListAsync(TransactionQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Transaction>>([]);

        public void Remove(Transaction transaction)
        {
        }

        public Task<IReadOnlyDictionary<string, string>> FindLatestCategoriesAsync(
            Guid ownerUserId, IReadOnlyCollection<string> descriptionKeys, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public Task<IReadOnlyList<Transaction>> ListByImportBatchAsync(
            Guid ownerUserId, Guid importBatchId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Transaction>>([]);
    }

    private sealed class RecordingOutbox : IEventOutbox
    {
        public List<TransactionEvent> Events { get; } = [];

        public void Add(TransactionEvent @event) => Events.Add(@event);
    }

    private sealed class AllAccountsExist : IAccountsClient
    {
        public Task<string?> FindAccountScopeAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("Personal");

        public Task<bool> CreditCardExistsAsync(Guid creditCardId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool?> IsAccountActiveAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult<bool?>(true);

        public Task<IReadOnlyList<InvoiceCardHint>> ListActiveCardsAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InvoiceCardHint>>([]);
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
