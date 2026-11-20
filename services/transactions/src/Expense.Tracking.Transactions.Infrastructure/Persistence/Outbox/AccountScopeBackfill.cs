using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Events;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Outbox;

/// <summary>Resultado do backfill: escopos resolvidos e lancamentos cuja conta o accounts nao conhece.</summary>
public sealed record AccountScopeBackfillResult(int Resolved, int Unresolved);

/// <summary>
/// EVT-4: completa o escopo PF/PJ dos lancamentos da carga inicial (<see cref="OutboxBackfill"/>),
/// cujo <see cref="TransactionRegistered"/> saiu com <c>accountScope = null</c>. Pergunta o escopo
/// ao accounts (token de servico) e grava um <see cref="TransactionAccountScopeResolved"/> por
/// lancamento. Idempotente: ignora lancamentos que ja tem o evento e os ja excluidos.
/// </summary>
public static class AccountScopeBackfill
{
    // Payload gravado pelo EventJson (camelCase, sem espacos) de um TransactionRegistered sem escopo.
    private const string UnknownScopeMarker = "\"accountScope\":null";

    public static async Task<AccountScopeBackfillResult> RunAsync(
        TransactionsDbContext dbContext,
        IAccountScopeDirectory directory,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        var pending = await dbContext.OutboxEvents
            .AsNoTracking()
            .Where(e => e.EventType == TransactionRegistered.Name && e.Payload.Contains(UnknownScopeMarker))
            .Where(e => !dbContext.OutboxEvents.Any(r =>
                r.AggregateId == e.AggregateId && r.EventType == TransactionAccountScopeResolved.Name))
            .Join(dbContext.Transactions, e => e.AggregateId, t => t.Id,
                (e, t) => new { e.Sequence, t.Id, t.OwnerUserId, t.AccountId })
            .OrderBy(p => p.Sequence)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return new AccountScopeBackfillResult(0, 0);
        }

        var scopes = await directory.GetScopesAsync(pending.Select(p => p.AccountId).Distinct().ToArray(), cancellationToken);

        var now = timeProvider.GetUtcNow();
        var resolved = 0;
        foreach (var transaction in pending)
        {
            if (!scopes.TryGetValue(transaction.AccountId, out var scope))
            {
                continue;
            }

            dbContext.OutboxEvents.Add(EventOutbox.ToOutboxEvent(
                new TransactionAccountScopeResolved(transaction.Id, transaction.OwnerUserId, transaction.AccountId, scope),
                now,
                traceParent: null));
            resolved++;
        }

        if (resolved > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new AccountScopeBackfillResult(resolved, pending.Count - resolved);
    }
}
