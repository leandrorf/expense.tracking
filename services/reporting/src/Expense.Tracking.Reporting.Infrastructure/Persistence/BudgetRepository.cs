using Expense.Tracking.Reporting.Application.Budgets;
using Expense.Tracking.Reporting.Domain.Budgets;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Reporting.Infrastructure.Persistence;

internal sealed class BudgetRepository(ReportingDbContext dbContext) : IBudgetRepository
{
    public Task<bool> ExistsAsync(Guid ownerUserId, BudgetScope scope, string categoryKey, CancellationToken cancellationToken) =>
        dbContext.Budgets.AnyAsync(
            b => b.OwnerUserId == ownerUserId && b.Scope == scope && b.CategoryKey == categoryKey,
            cancellationToken);

    public async Task<IReadOnlyList<Budget>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        await dbContext.Budgets.AsNoTracking()
            .Where(b => b.OwnerUserId == ownerUserId)
            .ToListAsync(cancellationToken);

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
            // Indice unico (dono, escopo, categoria normalizada): outra requisicao gravou antes.
            dbContext.Entry(budget).State = EntityState.Detached;
            return false;
        }
    }
}
