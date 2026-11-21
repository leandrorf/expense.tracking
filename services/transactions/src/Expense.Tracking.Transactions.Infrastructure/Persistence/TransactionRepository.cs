using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence;

internal sealed class TransactionRepository(TransactionsDbContext dbContext) : ITransactionRepository
{
    public Task<Transaction?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) =>
        dbContext.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.OwnerUserId == ownerUserId, cancellationToken);

    public async Task<IReadOnlyList<Transaction>> ListAsync(TransactionQuery query, CancellationToken cancellationToken)
    {
        var q = dbContext.Transactions.Where(t => t.OwnerUserId == query.OwnerUserId);

        if (query.AccountId is { } accountId)
        {
            q = q.Where(t => t.AccountId == accountId);
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            q = q.Where(t => t.Category == query.Category);
        }

        if (query.From is { } from)
        {
            q = q.Where(t => t.OccurredOn >= from);
        }

        if (query.To is { } to)
        {
            q = q.Where(t => t.OccurredOn <= to);
        }

        return await q.OrderByDescending(t => t.OccurredOn).ThenByDescending(t => t.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Transaction transaction, CancellationToken cancellationToken) =>
        await dbContext.Transactions.AddAsync(transaction, cancellationToken);

    public void Remove(Transaction transaction) => dbContext.Transactions.Remove(transaction);

    public async Task<IReadOnlySet<string>> FindExistingExternalIdsAsync(
        Guid ownerUserId, Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        // Em blocos, para o IN nao crescer com arquivos de milhares de linhas.
        foreach (var chunk in externalIds.Distinct().Chunk(500))
        {
            var existing = await dbContext.Transactions
                .Where(t => t.OwnerUserId == ownerUserId && t.AccountId == accountId &&
                            t.ExternalId != null && chunk.Contains(t.ExternalId))
                .Select(t => t.ExternalId!)
                .ToListAsync(cancellationToken);
            found.UnionWith(existing);
        }

        return found;
    }
}
