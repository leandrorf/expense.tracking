using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Outbox;

/// <summary>
/// INV-2 (ADR-0005, decisao 2): toda compra no cartao cujo <see cref="TransactionRegistered"/> saiu
/// na versao 1 (sem descricao, cartao e parcelas) ganha um <see cref="TransactionCardDetailsResolved"/>.
/// Os dados estao no proprio lancamento, entao roda na subida, sem depender de outro servico.
/// Idempotente: ignora lancamentos que ja tem um <see cref="TransactionRegistered"/> v2 ou o evento
/// de detalhes; os ja excluidos nao existem mais na tabela.
/// </summary>
public static class CardDetailsBackfill
{
    public static async Task<int> RunAsync(TransactionsDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken = default)
    {
        var pending = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.PaymentMethod == PaymentMethod.CreditCard && t.CreditCardId != null)
            .Where(t => !dbContext.OutboxEvents.Any(e => e.AggregateId == t.Id &&
                ((e.EventType == TransactionRegistered.Name && e.Version >= TransactionRegistered.CurrentVersion) ||
                 e.EventType == TransactionCardDetailsResolved.Name)))
            .OrderBy(t => t.CreatedAtUtc)
            .Select(t => new { t.Id, t.OwnerUserId, t.Description, CreditCardId = t.CreditCardId!.Value, t.Installments })
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return 0;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var t in pending)
        {
            dbContext.OutboxEvents.Add(EventOutbox.ToOutboxEvent(
                new TransactionCardDetailsResolved(t.Id, t.OwnerUserId, t.Description, t.CreditCardId, t.Installments),
                now,
                traceParent: null));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return pending.Count;
    }
}
