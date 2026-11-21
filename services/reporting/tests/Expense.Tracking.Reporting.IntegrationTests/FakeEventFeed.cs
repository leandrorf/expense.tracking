using System.Text.Json;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Projection;

namespace Expense.Tracking.Reporting.IntegrationTests;

/// <summary>
/// Feed de eventos falso com o mesmo comportamento do transactions: sequencia crescente, leitura
/// a partir de um cursor e <c>lastSequence</c> igual ao cursor quando nao ha nada novo.
/// </summary>
public sealed class FakeEventFeed : IEventFeedClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly List<FeedEvent> _events = [];

    public int Reads { get; private set; }

    public FeedEvent Registered(
        Guid transactionId, Guid ownerUserId, string? accountScope = "Personal", decimal amount = 100m,
        string category = "Alimentacao", Guid? accountId = null, string occurredOn = "2026-11-19",
        string transactionType = "Expense", string paymentMethod = "Pix", string currency = "BRL") =>
        Append(FeedEventTypes.TransactionRegistered, transactionId, ownerUserId, new
        {
            transactionId,
            ownerUserId,
            accountId = accountId ?? Guid.NewGuid(),
            accountScope,
            transactionType,
            paymentMethod,
            category,
            amount,
            currency,
            occurredOn,
        });

    /// <summary>INV-2: compra (ou estorno) no cartao, no formato v2 do TransactionRegistered.</summary>
    public FeedEvent CardPurchase(
        Guid transactionId, Guid ownerUserId, Guid cardId, decimal amount, string occurredOn, int installments = 1,
        string description = "Compra", string category = "Compras", string transactionType = "Expense") =>
        Append(FeedEventTypes.TransactionRegistered, transactionId, ownerUserId, new
        {
            transactionId,
            ownerUserId,
            accountId = Guid.NewGuid(),
            accountScope = "Personal",
            transactionType,
            paymentMethod = "CreditCard",
            category,
            amount,
            currency = "BRL",
            occurredOn,
            description,
            creditCardId = cardId,
            installments,
        }, version: 2);

    public FeedEvent ScopeResolved(Guid transactionId, Guid ownerUserId, string accountScope) =>
        Append(FeedEventTypes.TransactionAccountScopeResolved, transactionId, ownerUserId, new
        {
            transactionId,
            ownerUserId,
            accountId = Guid.NewGuid(),
            accountScope,
        });

    public FeedEvent Deleted(Guid transactionId, Guid ownerUserId) =>
        Append(FeedEventTypes.TransactionDeleted, transactionId, ownerUserId, new { transactionId, ownerUserId });

    /// <summary>INV-6: pagamento de fatura (<c>CardInvoicePaid</c> v1).</summary>
    public FeedEvent InvoicePaid(
        Guid paymentId, Guid ownerUserId, Guid cardId, string invoiceDueMonth, decimal amount, string paidOn = "2026-11-20") =>
        Append(FeedEventTypes.CardInvoicePaid, paymentId, ownerUserId, new
        {
            paymentId,
            ownerUserId,
            creditCardId = cardId,
            invoiceDueMonth,
            accountId = Guid.NewGuid(),
            amount,
            currency = "BRL",
            paidOn,
        });

    public FeedEvent InvoicePaymentDeleted(Guid paymentId, Guid ownerUserId, Guid cardId) =>
        Append(FeedEventTypes.CardInvoicePaymentDeleted, paymentId, ownerUserId, new { paymentId, ownerUserId, creditCardId = cardId });

    public Task<FeedPage> ReadAsync(long afterSequence, int limit, CancellationToken cancellationToken)
    {
        lock (_events)
        {
            Reads++;
            var page = _events.Where(e => e.Sequence > afterSequence).Take(limit).ToArray();
            return Task.FromResult(new FeedPage(page, page.Length > 0 ? page[^1].Sequence : afterSequence, _events.Count));
        }
    }

    /// <summary>Os payloads deste fake tem o formato v1 de cada evento.</summary>
    private FeedEvent Append(string type, Guid aggregateId, Guid ownerUserId, object payload, int version = 1)
    {
        lock (_events)
        {
            var feedEvent = new FeedEvent(
                _events.Count + 1, Guid.NewGuid(), type, version, aggregateId, ownerUserId,
                DateTimeOffset.UtcNow, null, JsonSerializer.SerializeToElement(payload, Json));
            _events.Add(feedEvent);
            return feedEvent;
        }
    }
}
