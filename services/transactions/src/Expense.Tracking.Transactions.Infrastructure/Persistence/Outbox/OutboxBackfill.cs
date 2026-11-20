using Expense.Tracking.Transactions.Application.Events;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Outbox;

/// <summary>
/// EVT-1: carga inicial do outbox. Grava um <see cref="TransactionRegistered"/> para cada
/// lancamento que ainda nao tem um, em ordem de criacao. Roda na subida do servico e e
/// idempotente: na segunda execucao nao encontra nada a fazer.
/// <para>
/// O transactions nao guarda o escopo PF/PJ da conta, entao esses eventos saem com
/// <c>accountScope = null</c>. So afeta lancamentos anteriores ao outbox.
/// </para>
/// </summary>
public static class OutboxBackfill
{
    public static async Task<int> RunAsync(TransactionsDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken = default)
    {
        var pending = await dbContext.Transactions
            .Where(t => !dbContext.OutboxEvents.Any(e =>
                e.AggregateId == t.Id && e.EventType == TransactionRegistered.Name))
            .OrderBy(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return 0;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var transaction in pending)
        {
            dbContext.OutboxEvents.Add(EventOutbox.ToOutboxEvent(
                TransactionRegistered.From(transaction, accountScope: null), now, traceParent: null));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return pending.Count;
    }
}
