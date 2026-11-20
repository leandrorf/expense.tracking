using Expense.Tracking.Reporting.Application.Budgets;
using Expense.Tracking.Reporting.Domain.Budgets;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Reporting.Infrastructure.Persistence;

internal sealed class BudgetRepository(ReportingDbContext dbContext) : IBudgetRepository
{
    // Mesma regra de Budget.OverlapsFrom, escrita aqui para o EF traduzir em SQL.
    public Task<bool> OverlapsAsync(
        Guid ownerUserId, BudgetScope scope, string categoryKey, DateOnly startMonth, CancellationToken cancellationToken) =>
        dbContext.Budgets.AnyAsync(
            b => b.OwnerUserId == ownerUserId && b.Scope == scope && b.CategoryKey == categoryKey
                && (b.ClosedFromMonth == null || b.ClosedFromMonth > startMonth),
            cancellationToken);

    public async Task<IReadOnlyList<Budget>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        await dbContext.Budgets.AsNoTracking()
            .Where(b => b.OwnerUserId == ownerUserId)
            .ToListAsync(cancellationToken);

    public Task<Budget?> FindAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) =>
        dbContext.Budgets.FirstOrDefaultAsync(b => b.Id == id && b.OwnerUserId == ownerUserId, cancellationToken);

    public async Task<bool> TryAddAsync(Budget budget, CancellationToken cancellationToken)
    {
        dbContext.Budgets.Add(budget);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Indice unico (dono, escopo, categoria aberta): outra requisicao gravou antes.
            dbContext.Entry(budget).State = EntityState.Detached;
            return false;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
