using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.CardPayments;
using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Domain.CardPayments;

using Expense.Tracking.Transactions.Domain.Imports;

namespace Expense.Tracking.Transactions.UnitTests;

/// <summary>INV-6 (ADR-0005, decisao 6): pagamento de fatura e o evento no mesmo commit.</summary>
public sealed class CardPaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 12, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.NewGuid();

    [Fact]
    public void Create_NormalizesTheInvoiceMonthToItsFirstDay_AndDefaultsToBrl()
    {
        var payment = CardPayment.Create(Owner, Guid.NewGuid(), new DateOnly(2026, 12, 17), Guid.NewGuid(),
            99.9m, null, new DateOnly(2026, 12, 15), Now).Value;

        Assert.Equal((new DateOnly(2026, 12, 1), "BRL", 99.9m), (payment.InvoiceDueMonth, payment.Currency, payment.Amount));
    }

    [Theory]
    [InlineData(0, "money.amount_invalid")]
    [InlineData(10.001, "money.amount_precision")]
    public void Create_RejectsInvalidAmounts(decimal amount, string code)
    {
        var result = CardPayment.Create(Owner, Guid.NewGuid(), new DateOnly(2026, 12, 1), Guid.NewGuid(),
            amount, "BRL", new DateOnly(2026, 12, 15), Now);

        Assert.Equal(code, result.Error.Code);
    }

    [Fact]
    public void Create_RequiresCardAndAccount()
    {
        var result = CardPayment.Create(Owner, Guid.Empty, new DateOnly(2026, 12, 1), Guid.NewGuid(),
            10m, "BRL", new DateOnly(2026, 12, 15), Now);

        Assert.Equal("card_payment.references_required", result.Error.Code);
    }

    [Fact]
    public async Task Register_AddsCardInvoicePaid_BeforeSaving()
    {
        var outbox = new RecordingOutbox();
        var unitOfWork = new CountingUnitOfWork(outbox);
        var handler = new RegisterCardPaymentHandler(new InMemoryPayments(), new StubAccounts(true), outbox, unitOfWork, new FixedTime(Now));
        var card = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new RegisterCardPaymentCommand(Owner, card, new DateOnly(2026, 12, 1), Guid.NewGuid(), 500m, "BRL", new DateOnly(2026, 12, 15)),
            CancellationToken.None);

        var paid = Assert.IsType<CardInvoicePaid>(Assert.Single(outbox.Events));
        Assert.Equal((result.Value.Id, card, "2026-12", 500m), (paid.PaymentId, paid.CreditCardId, paid.InvoiceDueMonth, paid.Amount));
        Assert.Equal(1, outbox.EventsAtFirstSave);
    }

    [Fact]
    public async Task Register_WithAnInactiveAccount_AddsNoEventAndDoesNotSave()
    {
        var outbox = new RecordingOutbox();
        var unitOfWork = new CountingUnitOfWork(outbox);
        var handler = new RegisterCardPaymentHandler(new InMemoryPayments(), new StubAccounts(false), outbox, unitOfWork, new FixedTime(Now));

        var result = await handler.HandleAsync(
            new RegisterCardPaymentCommand(Owner, Guid.NewGuid(), new DateOnly(2026, 12, 1), Guid.NewGuid(), 500m, null, new DateOnly(2026, 12, 15)),
            CancellationToken.None);

        Assert.Equal("card_payment.account_inactive", result.Error.Code);
        Assert.Empty(outbox.Events);
        Assert.Null(outbox.EventsAtFirstSave);
    }

    internal sealed class InMemoryPayments : ICardPaymentRepository
    {
        private readonly List<CardPayment> _items = [];

        public Task<CardPayment?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult(_items.FirstOrDefault(p => p.Id == id && p.OwnerUserId == ownerUserId));

        public Task<IReadOnlyList<CardPayment>> ListAsync(
            Guid ownerUserId, Guid? creditCardId, DateOnly? invoiceDueMonth, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CardPayment>>(_items.Where(p => p.OwnerUserId == ownerUserId).ToList());

        public Task AddAsync(CardPayment payment, CancellationToken cancellationToken)
        {
            _items.Add(payment);
            return Task.CompletedTask;
        }

        public void Remove(CardPayment payment) => _items.Remove(payment);

        public Task<IReadOnlySet<string>> FindExistingExternalIdsAsync(
            Guid ownerUserId, Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(_items
                .Where(p => p.OwnerUserId == ownerUserId && p.AccountId == accountId && p.ExternalId is not null
                    && externalIds.Contains(p.ExternalId))
                .Select(p => p.ExternalId!)
                .ToHashSet());

        public Task<IReadOnlyList<CardPayment>> ListByImportBatchAsync(Guid ownerUserId, Guid importBatchId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CardPayment>>(_items.Where(p => p.OwnerUserId == ownerUserId && p.ImportBatchId == importBatchId).ToList());
    }

    private sealed class RecordingOutbox : IEventOutbox
    {
        public List<TransactionEvent> Events { get; } = [];

        public int? EventsAtFirstSave { get; set; }

        public void Add(TransactionEvent @event) => Events.Add(@event);
    }

    private sealed class CountingUnitOfWork(RecordingOutbox outbox) : IUnitOfWork
    {
        public void Reset()
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            outbox.EventsAtFirstSave ??= outbox.Events.Count;
            return Task.FromResult(1);
        }
    }

    private sealed class StubAccounts(bool accountActive) : IAccountsClient
    {
        public Task<string?> FindAccountScopeAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult<string?>("Personal");

        public Task<bool> CreditCardExistsAsync(Guid creditCardId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool?> IsAccountActiveAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult<bool?>(accountActive);

        public Task<IReadOnlyList<InvoiceCardHint>> ListActiveCardsAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InvoiceCardHint>>([]);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
